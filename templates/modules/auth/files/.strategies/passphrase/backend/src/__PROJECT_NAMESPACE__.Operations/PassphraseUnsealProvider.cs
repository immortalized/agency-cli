using System.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Operations;

public sealed class PassphraseUnsealProvider : IUnsealMaterialProvider
{
    private readonly string _bundlePath;

    public PassphraseUnsealProvider()
    {
        _bundlePath = Path.Combine(
            OperationsEnvironment.FromEnvironment().MaterialDirectory,
            "unseal-share.v1.json");
    }

    public string Strategy => "passphrase";

    public UnsealMaterialDiagnostic GetMaterialDiagnostic(
        OpenBaoSealStatus status)
    {
        var present = File.Exists(_bundlePath)
            || File.Exists(InitializationStagedBundlePath);

        if (!status.Initialized)
        {
            return present
                ? new(
                    false,
                    "OpenBao is not initialized, but encrypted unseal material exists. Remove it only after confirming the OpenBao storage is empty.")
                : new(true, "Not initialized; no unseal-share bundle is expected yet.");
        }

        return present
            ? new(true, "The encrypted single-share unseal bundle is present.")
            : new(
                false,
                "OpenBao is initialized but its encrypted unseal-share bundle is missing. This instance cannot be unsealed; if the share cannot be recovered, wipe the OpenBao storage and bundle state and reinitialize.");
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

        var passphrase = ConsoleSecretReader.ReadRequired("New OpenBao unseal passphrase: ");
        byte[]? confirmation = null;
        try
        {
            confirmation = ConsoleSecretReader.ReadRequired("Confirm OpenBao unseal passphrase: ");
            if (!CryptographicOperations.FixedTimeEquals(passphrase, confirmation))
            {
                throw new InvalidOperationException("The passphrases do not match.");
            }

            await PreflightEncryptedBundleStorageAsync(
                passphrase,
                cancellationToken);

            var result = await client.InitializeAsync(
                1,
                1,
                autoSeal: false,
                cancellationToken: cancellationToken);
            try
            {
                await WriteInitializationBundleUntilSuccessfulAsync(
                    result.Shares.Single(),
                    passphrase);
                PromoteInitializationStaging();
                var unsealed = await client.SubmitUnsealShareAsync(
                    result.Shares.Single(),
                    cancellationToken: CancellationToken.None);
                if (unsealed.Sealed)
                {
                    throw new InvalidOperationException("OpenBao remained sealed after its one required share was submitted.");
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
            CryptographicOperations.ZeroMemory(passphrase);
            if (confirmation is not null)
            {
                CryptographicOperations.ZeroMemory(confirmation);
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

        var passphrase = ConsoleSecretReader.ReadRequired("OpenBao unseal passphrase: ");
        byte[]? share = null;
        try
        {
            share = await EncryptedUnsealBundle.ReadAsync(_bundlePath, passphrase, cancellationToken);
            status = await client.SubmitUnsealShareAsync(share, cancellationToken: cancellationToken);
            if (status.Sealed)
            {
                throw new InvalidOperationException("OpenBao remained sealed after the configured share was submitted.");
            }
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

    public async Task RekeyAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default)
    {
        var passphrase = ConsoleSecretReader.ReadRequired("Current OpenBao unseal passphrase: ");
        byte[]? oldShare = null;
        try
        {
            oldShare = await EncryptedUnsealBundle.ReadAsync(_bundlePath, passphrase, cancellationToken);
            var nonce = await client.BeginRekeyAsync(1, 1, cancellationToken);
            var result = await client.SubmitRekeyShareAsync(oldShare, nonce, cancellationToken);
            if (!result.Complete
                || !result.VerificationRequired
                || result.NewShares.Count != 1)
            {
                throw new InvalidOperationException("OpenBao did not complete the native rekey operation.");
            }

            var replacement = ConsoleSecretReader.ReadRequired("New OpenBao unseal passphrase: ");
            byte[]? confirmation = null;
            try
            {
                confirmation = ConsoleSecretReader.ReadRequired("Confirm new OpenBao unseal passphrase: ");
                if (!CryptographicOperations.FixedTimeEquals(replacement, confirmation))
                {
                    throw new InvalidOperationException("The new passphrases do not match.");
                }
                var pendingPath = $"{_bundlePath}.pending";
                await EncryptedUnsealBundle.WriteAsync(
                    pendingPath,
                    result.NewShares.Single(),
                    replacement,
                    overwrite: true,
                    cancellationToken);

                var verified = await client.SubmitRekeyVerificationShareAsync(
                    result.NewShares.Single(),
                    result.Nonce,
                    cancellationToken);
                if (!verified)
                {
                    throw new InvalidOperationException(
                        "OpenBao did not finalize replacement-share verification.");
                }

                File.Move(pendingPath, _bundlePath, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(replacement);
                if (confirmation is not null)
                {
                    CryptographicOperations.ZeroMemory(confirmation);
                }
                foreach (var share in result.NewShares)
                {
                    CryptographicOperations.ZeroMemory(share);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passphrase);
            if (oldShare is not null)
            {
                CryptographicOperations.ZeroMemory(oldShare);
            }
        }
    }

    private async Task PreflightEncryptedBundleStorageAsync(
        ReadOnlyMemory<byte> passphrase,
        CancellationToken cancellationToken)
    {
        var placeholder = RandomNumberGenerator.GetBytes(64);
        try
        {
            await EncryptedUnsealBundle.WriteAsync(
                PreflightPath,
                placeholder,
                passphrase,
                overwrite: true,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(placeholder);
            File.Delete(PreflightPath);
            var directory = Path.GetDirectoryName(PreflightPath)!;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    private async Task WriteInitializationBundleUntilSuccessfulAsync(
        ReadOnlyMemory<byte> share,
        ReadOnlyMemory<byte> passphrase)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await EncryptedUnsealBundle.WriteAsync(
                    InitializationStagedBundlePath,
                    share,
                    passphrase,
                    overwrite: true,
                    CancellationToken.None);
                return;
            }
            catch (Exception exception)
                when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(
                    $"The encrypted unseal share could not be staged (attempt {attempt}): {exception.Message}");
                Console.Error.WriteLine(
                    "OpenBao is now initialized and this process holds the only plaintext copy. Do not terminate it; correct the storage problem and the write will retry.");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private void PromoteInitializationStaging()
    {
        if (!File.Exists(InitializationStagedBundlePath))
        {
            return;
        }

        if (File.Exists(_bundlePath))
        {
            File.Delete(InitializationStagedBundlePath);
        }
        else
        {
            File.Move(
                InitializationStagedBundlePath,
                _bundlePath,
                overwrite: false);
        }

        var stagingDirectory = Path.GetDirectoryName(
            InitializationStagedBundlePath)!;
        if (Directory.Exists(stagingDirectory)
            && !Directory.EnumerateFileSystemEntries(
                    stagingDirectory)
                .Any())
        {
            Directory.Delete(stagingDirectory, recursive: false);
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

    private string InitializationStagedBundlePath =>
        Path.Combine(
            Path.GetDirectoryName(_bundlePath)!,
            ".init-staging",
            Path.GetFileName(_bundlePath));

    private string PreflightPath =>
        Path.Combine(
            Path.GetDirectoryName(_bundlePath)!,
            ".init-preflight",
            "unseal-share.probe");
}
