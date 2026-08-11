using System.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Operations;

public sealed class MultiPassphraseUnsealProvider : IUnsealMaterialProvider
{
    private const int Shares = __UNSEAL_KEY_SHARES__;
    private const int Threshold = __UNSEAL_KEY_THRESHOLD__;
    private const int ConfirmationAttempts = 3;
    private readonly string _materialDirectory;
    private readonly BundleWriter _bundleWriter;
    private readonly Func<string, byte[]> _secretReader;
    private readonly Action<string> _reportError;

    public MultiPassphraseUnsealProvider()
        : this(
            OperationsEnvironment.FromEnvironment().MaterialDirectory,
            EncryptedUnsealBundle.WriteAsync,
            ConsoleSecretReader.ReadRequired,
            Console.Error.WriteLine)
    {
    }

    internal MultiPassphraseUnsealProvider(
        string materialDirectory,
        BundleWriter bundleWriter,
        Func<string, byte[]>? secretReader = null,
        Action<string>? reportError = null)
    {
        _materialDirectory = Path.GetFullPath(materialDirectory);
        _bundleWriter = bundleWriter
            ?? throw new ArgumentNullException(nameof(bundleWriter));
        _secretReader = secretReader
            ?? ConsoleSecretReader.ReadRequired;
        _reportError = reportError
            ?? Console.Error.WriteLine;
    }

    public string Strategy => "multi-passphrase";

    public UnsealMaterialDiagnostic GetMaterialDiagnostic(
        OpenBaoSealStatus status)
    {
        var missing = Enumerable.Range(1, Shares)
            .Where(number =>
                !File.Exists(BundlePath(number))
                && !File.Exists(InitializationStagedBundlePath(number)))
            .ToArray();

        if (!status.Initialized)
        {
            return missing.Length == Shares
                ? new(true, "Not initialized; no unseal-share bundles are expected yet.")
                : new(
                    false,
                    "OpenBao is not initialized, but partial unseal-share material exists. Remove the stale material only after confirming the OpenBao storage is empty.");
        }

        if (missing.Length > 0)
        {
            var available = Shares - missing.Length;
            if (available >= Threshold)
            {
                return new(
                    true,
                    $"OpenBao is initialized with a degraded recovery set: {missing.Length} of {Shares} expected unseal-share bundles are missing (shares {string.Join(", ", missing)}), but {available} available bundles still meet threshold {Threshold}. Unseal with the available quorum and immediately run 'openbao rekey' to restore all {Shares} operator bundles.");
            }

            return new(
                false,
                $"OpenBao is initialized but {missing.Length} of {Shares} expected unseal-share bundles are missing (shares {string.Join(", ", missing)}). Only {available} bundles remain, below threshold {Threshold}; this instance cannot be unsealed. If the missing shares cannot be recovered, wipe the OpenBao storage and all bundles and reinitialize.");
        }

        var stagedCount = Enumerable.Range(1, Shares)
            .Count(number => File.Exists(InitializationStagedBundlePath(number)));

        return stagedCount == 0
            ? new(true, $"All {Shares} encrypted unseal-share bundles are present.")
            : new(
                true,
                $"All {Shares} encrypted shares are recoverable; {stagedCount} await atomic promotion from initialization staging.");
    }

    public async Task<OpenBaoInitializationResult?> InitializeIfNeededAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default)
    {
        var status = await client.GetStatusAsync(cancellationToken);
        if (status.Initialized)
        {
            PromoteInitializationStaging();
            ThrowIfMaterialIsIncomplete(status);
            return null;
        }

        ThrowIfMaterialIsIncomplete(status);

        var passphrases = new byte[Shares][];
        try
        {
            for (var number = 1; number <= Shares; number++)
            {
                Console.WriteLine(
                    $"Operator {number} of {Shares}: confirm the passphrase before OpenBao initialization begins.");
                passphrases[number - 1] = ReadConfirmedPassphrase(
                    number,
                    isReplacement: false,
                    _secretReader,
                    _reportError);
            }

            await PreflightEncryptedBundleStorageAsync(
                passphrases,
                cancellationToken);

            // From this call until all encrypted staging files exist, do not
            // allow an ordinary typo, cancellation, or transient write error
            // to escape and destroy the only copy of an unpersisted share.
            var result = await client.InitializeAsync(
                Shares,
                Threshold,
                autoSeal: false,
                cancellationToken);
            try
            {
                for (var number = 1; number <= Shares; number++)
                {
                    await WriteInitializationBundleUntilSuccessfulAsync(
                        number,
                        result.Shares[number - 1],
                        passphrases[number - 1]);
                }

                PromoteInitializationStaging();

                for (var number = 1; number <= Threshold; number++)
                {
                    var statusAfterShare = await client.SubmitUnsealShareAsync(
                        result.Shares[number - 1],
                        cancellationToken: CancellationToken.None);
                    CryptographicOperations.ZeroMemory(result.Shares[number - 1]);
                    if (number < Threshold && !statusAfterShare.Sealed)
                    {
                        throw new InvalidOperationException("OpenBao accepted fewer shares than the configured threshold.");
                    }
                    if (number == Threshold && statusAfterShare.Sealed)
                    {
                        throw new InvalidOperationException("OpenBao remained sealed after the threshold was reached.");
                    }
                }

                return result;
            }
            finally
            {
                foreach (var share in result.Shares)
                {
                    CryptographicOperations.ZeroMemory(share);
                }
            }
        }
        finally
        {
            foreach (var passphrase in passphrases)
            {
                if (passphrase is not null)
                {
                    CryptographicOperations.ZeroMemory(passphrase);
                }
            }
        }
    }

    public async Task UnsealAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default)
    {
        var status = await client.GetStatusAsync(cancellationToken);
        PromoteInitializationStaging();
        ThrowIfMaterialIsIncomplete(status);
        if (!status.Sealed)
        {
            Console.WriteLine("OpenBao is already unsealed.");
            return;
        }

        Console.Write(
            $"This operation requires {Threshold} distinct operators. Confirm all are present [y/N]: ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Insufficient shares: {Threshold} operators must be present before unseal begins.");
        }

        var used = new HashSet<int>();
        for (var submitted = 0; submitted < Threshold; submitted++)
        {
            var number = ReadOperatorNumber(used);
            used.Add(number);
            var passphrase = ConsoleSecretReader.ReadRequired(
                $"Operator {number} passphrase: ");
            byte[]? share = null;
            try
            {
                share = await EncryptedUnsealBundle.ReadAsync(
                    BundlePath(number),
                    passphrase,
                    cancellationToken);
                status = await client.SubmitUnsealShareAsync(
                    share,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passphrase);
                if (share is not null)
                {
                    CryptographicOperations.ZeroMemory(share);
                }
            }
        }

        if (status.Sealed)
        {
            throw new InvalidOperationException("OpenBao remained sealed after the configured threshold was submitted.");
        }
    }

    public async Task RekeyAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine(
            $"Native OpenBao rekey requires {Threshold} existing operators and will generate {Shares} new shares.");
        var nonce = await client.BeginRekeyAsync(Shares, Threshold, cancellationToken);
        OpenBaoRekeyProgress? progress = null;
        var used = new HashSet<int>();

        for (var submitted = 0; submitted < Threshold; submitted++)
        {
            var number = ReadOperatorNumber(used);
            used.Add(number);
            var passphrase = ConsoleSecretReader.ReadRequired($"Current operator {number} passphrase: ");
            byte[]? share = null;
            try
            {
                share = await EncryptedUnsealBundle.ReadAsync(BundlePath(number), passphrase, cancellationToken);
                progress = await client.SubmitRekeyShareAsync(share, nonce, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passphrase);
                if (share is not null)
                {
                    CryptographicOperations.ZeroMemory(share);
                }
            }
        }

        if (progress is null
            || !progress.Complete
            || !progress.VerificationRequired
            || progress.NewShares.Count != Shares)
        {
            throw new InvalidOperationException("OpenBao did not return the complete replacement share set.");
        }

        try
        {
            for (var number = 1; number <= Shares; number++)
            {
                var passphrase = ReadConfirmedPassphrase(
                    number,
                    isReplacement: true,
                    _secretReader,
                    _reportError);
                try
                {
                    await EncryptedUnsealBundle.WriteAsync(
                        PendingBundlePath(number),
                        progress.NewShares[number - 1],
                        passphrase,
                        overwrite: true,
                        cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passphrase);
                }
            }

            var verificationComplete = false;
            for (var number = 1; number <= Threshold; number++)
            {
                verificationComplete = await client.SubmitRekeyVerificationShareAsync(
                    progress.NewShares[number - 1],
                    progress.Nonce,
                    cancellationToken);
            }

            if (!verificationComplete)
            {
                throw new InvalidOperationException(
                    "OpenBao did not finalize replacement-share verification.");
            }

            for (var number = 1; number <= Shares; number++)
            {
                File.Move(
                    PendingBundlePath(number),
                    BundlePath(number),
                    overwrite: true);
            }
        }
        finally
        {
            foreach (var share in progress.NewShares)
            {
                CryptographicOperations.ZeroMemory(share);
            }
        }
    }

    internal static byte[] ReadConfirmedPassphrase(
        int number,
        bool isReplacement,
        Func<string, byte[]>? secretReader = null,
        Action<string>? reportError = null)
    {
        secretReader ??= ConsoleSecretReader.ReadRequired;
        reportError ??= Console.Error.WriteLine;
        var label = isReplacement ? "new " : string.Empty;
        for (var attempt = 1; attempt <= ConfirmationAttempts; attempt++)
        {
            var first = secretReader($"Operator {number} {label}passphrase: ");
            byte[]? confirmation = null;
            var accepted = false;
            try
            {
                confirmation = secretReader($"Confirm operator {number} {label}passphrase: ");
                if (CryptographicOperations.FixedTimeEquals(first, confirmation))
                {
                    accepted = true;
                    return first;
                }

                reportError(
                    $"Operator {number} passphrases did not match. Type them identically; {ConfirmationAttempts - attempt} attempt(s) remain.");
            }
            finally
            {
                if (!accepted)
                {
                    CryptographicOperations.ZeroMemory(first);
                }
                if (confirmation is not null)
                {
                    CryptographicOperations.ZeroMemory(confirmation);
                }
            }
        }

        throw new InvalidOperationException(
            $"Operator {number} passphrase confirmation failed after {ConfirmationAttempts} attempts. OpenBao was not initialized; correct the input and run the command again.");
    }

    private async Task PreflightEncryptedBundleStorageAsync(
        IReadOnlyList<byte[]> passphrases,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(PreflightDirectory);
        var placeholder = RandomNumberGenerator.GetBytes(64);
        try
        {
            for (var number = 1; number <= Shares; number++)
            {
                await _bundleWriter(
                    Path.Combine(PreflightDirectory, $"share-{number:D2}.probe"),
                    placeholder,
                    passphrases[number - 1],
                    true,
                    cancellationToken);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(placeholder);
            if (Directory.Exists(PreflightDirectory))
            {
                Directory.Delete(PreflightDirectory, recursive: true);
            }
        }
    }

    internal async Task WriteInitializationBundleUntilSuccessfulAsync(
        int number,
        ReadOnlyMemory<byte> share,
        ReadOnlyMemory<byte> passphrase)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await _bundleWriter(
                    InitializationStagedBundlePath(number),
                    share,
                    passphrase,
                    true,
                    CancellationToken.None);
                return;
            }
            catch (Exception exception)
                when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(
                    $"Encrypted share {number} could not be staged (attempt {attempt}): {exception.Message}");
                Console.Error.WriteLine(
                    "OpenBao is now initialized and this process holds the only plaintext copy. Do not terminate it; correct the storage problem and the write will retry.");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private void PromoteInitializationStaging()
    {
        for (var number = 1; number <= Shares; number++)
        {
            var staged = InitializationStagedBundlePath(number);
            var final = BundlePath(number);
            if (!File.Exists(staged))
            {
                continue;
            }

            if (File.Exists(final))
            {
                File.Delete(staged);
            }
            else
            {
                File.Move(staged, final, overwrite: false);
            }
        }

        if (Directory.Exists(InitializationStagingDirectory)
            && !Directory.EnumerateFileSystemEntries(
                    InitializationStagingDirectory)
                .Any())
        {
            Directory.Delete(InitializationStagingDirectory, recursive: false);
        }
    }

    private void ThrowIfMaterialIsIncomplete(OpenBaoSealStatus status)
    {
        var diagnostic = GetMaterialDiagnostic(status);
        if (!diagnostic.IsUsable)
        {
            throw new InvalidOperationException(diagnostic.Message);
        }
    }

    private static int ReadOperatorNumber(IReadOnlySet<int> used)
    {
        Console.Write($"Operator share number (1-{Shares}): ");
        if (!int.TryParse(Console.ReadLine(), out var number)
            || number < 1
            || number > Shares
            || used.Contains(number))
        {
            throw new InvalidOperationException("A valid, unused operator share number is required.");
        }
        return number;
    }

    private string BundlePath(int number) => Path.Combine(
        _materialDirectory,
        $"unseal-share-{number:D2}.v1.json");

    private string PendingBundlePath(int number) =>
        $"{BundlePath(number)}.pending";

    private string InitializationStagedBundlePath(int number) =>
        Path.Combine(
            InitializationStagingDirectory,
            $"unseal-share-{number:D2}.v1.json");

    private string InitializationStagingDirectory =>
        Path.Combine(_materialDirectory, ".init-staging");

    private string PreflightDirectory =>
        Path.Combine(_materialDirectory, ".init-preflight");

    internal delegate Task BundleWriter(
        string path,
        ReadOnlyMemory<byte> share,
        ReadOnlyMemory<byte> passphrase,
        bool overwrite,
        CancellationToken cancellationToken);
}
