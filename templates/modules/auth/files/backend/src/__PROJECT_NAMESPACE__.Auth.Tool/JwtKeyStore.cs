using System.Security.Cryptography;
using System.Text.Json;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class JwtKeyStore
{
    private const int KeyRingVersion = 2;

    private const string KeyRingFileName =
        "auth-jwt-key-ring.json";

    private const string LockFileName =
        ".auth-key-operation.lock";

    private static readonly JsonSerializerOptions
        JsonOptions = new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

    public static async Task<JwtKeyOperationResult>
        InitializeAsync(
            string keyDirectory,
            CancellationToken cancellationToken = default)
    {
        var options = OpenBaoToolOptions
            .FromEnvironment();

        var paths = PreparePaths(
            keyDirectory,
            options.RuntimeTokenFile);

        using var operationLock =
            AcquireOperationLock(paths.LockFile);

        using var httpClient = CreateHttpClient(options);
        var client = new OpenBaoTransitAdminClient(
            httpClient,
            options);

        await client.EnsureTransitEnabledAsync(
            cancellationToken);

        var key = await client.ReadKeyAsync(
            cancellationToken);

        var initializationState =
            GetInitializationState(
            paths,
            key);

        if (initializationState ==
            InitializationState.Configured)
        {
            await OpenBaoRuntimePolicyVerifier
                .VerifyAsync(cancellationToken);

            var existingKeyRing = CreateKeyRing(
                options.KeyName,
                key!);

            ValidateKeyRing(existingKeyRing);

            await WriteKeyRingAsync(
                paths.KeyRingFile,
                existingKeyRing,
                cancellationToken);

            return new JwtKeyOperationResult(
                existingKeyRing.ActiveKeyId,
                existingKeyRing.Keys.Count);
        }

        if (initializationState ==
            InitializationState.FreshStorage)
        {
            await client.CreateKeyAsync(
                cancellationToken);

            key = await client.ReadKeyAsync(
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "OpenBao did not return the newly created Transit key.");
        }

        var configuredKey = key
            ?? throw new InvalidOperationException(
                "OpenBao JWT signing state is inconsistent.");

        var databaseOptions = DatabaseBootstrapOptions
            .FromEnvironment();

        var databaseClient =
            new OpenBaoDatabaseAdminClient(
                httpClient,
                options,
                databaseOptions);

        if (initializationState ==
            InitializationState.FreshStorage)
        {
            var managementCredential =
                await PostgresRoleBootstrapper
                    .BootstrapAsync(
                        databaseOptions,
                        cancellationToken);

            await databaseClient.ConfigureAsync(
                managementCredential,
                cancellationToken);
        }
        else
        {
            // Existing OpenBao state owns the current
            // management/runtime database passwords. Prove
            // that configuration is usable without rotating
            // or rewriting either PostgreSQL role.
            await databaseClient.ReadRuntimeCredentialAsync(
                cancellationToken);
        }

        await client.WriteRuntimePolicyAsync(
            cancellationToken);

        var keyRing = CreateKeyRing(
            options.KeyName,
            configuredKey);

        ValidateKeyRing(keyRing);

        var runtimeToken =
            await client.CreateRuntimeTokenAsync(
                cancellationToken);

        await WriteSensitiveFileAsync(
            paths.RuntimeTokenFile,
            $"{runtimeToken}{Environment.NewLine}",
            cancellationToken);

        await WriteKeyRingAsync(
            paths.KeyRingFile,
            keyRing,
            cancellationToken);

        return new JwtKeyOperationResult(
            keyRing.ActiveKeyId,
            keyRing.Keys.Count);
    }

    private static InitializationState
        GetInitializationState(
        KeyStorePaths paths,
        OpenBaoTransitKey? key)
    {
        if (key is null)
        {
            // A deliberately deleted OpenBao volume can leave
            // host-side public/runtime artifacts behind. They
            // are replaced only while bootstrapping a fresh
            // OpenBao store.
            return InitializationState.FreshStorage;
        }

        var keyRingExists = File.Exists(
            paths.KeyRingFile);

        var runtimeTokenExists = File.Exists(
            paths.RuntimeTokenFile);

        if (keyRingExists && runtimeTokenExists)
        {
            return InitializationState.Configured;
        }

        if (keyRingExists || runtimeTokenExists)
        {
            throw new InvalidOperationException(
                "JWT signing initialization is incomplete: the local key ring and runtime token must either both exist or both be absent.");
        }

        return InitializationState.MissingLocalArtifacts;
    }

    public static async Task<JwtKeyOperationResult>
        RotateAsync(
            string keyDirectory,
            CancellationToken cancellationToken = default)
    {
        var options = OpenBaoToolOptions
            .FromEnvironment();

        var paths = PreparePaths(
            keyDirectory,
            options.RuntimeTokenFile);

        using var operationLock =
            AcquireOperationLock(paths.LockFile);

        if (!File.Exists(paths.KeyRingFile))
        {
            throw new InvalidOperationException(
                "JWT signing has not been initialized. Run 'keys init' first.");
        }

        using var httpClient = CreateHttpClient(options);
        var client = new OpenBaoTransitAdminClient(
            httpClient,
            options);

        var currentKey = await client.ReadKeyAsync(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The OpenBao Transit signing key does not exist. Run 'keys init' to bootstrap the development instance.");

        await client.RotateKeyAsync(
            cancellationToken);

        var rotatedKey = await client.ReadKeyAsync(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "OpenBao did not return the rotated Transit key.");

        if (rotatedKey.LatestVersion
            != currentKey.LatestVersion + 1)
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned an unexpected key version after rotation.");
        }

        var keyRing = CreateKeyRing(
            options.KeyName,
            rotatedKey);

        ValidateKeyRing(keyRing);

        await WriteKeyRingAsync(
            paths.KeyRingFile,
            keyRing,
            cancellationToken);

        return new JwtKeyOperationResult(
            keyRing.ActiveKeyId,
            keyRing.Keys.Count);
    }

    private static HttpClient CreateHttpClient(
        OpenBaoToolOptions options)
    {
        return new HttpClient
        {
            BaseAddress = options.Address,
            Timeout = options.RequestTimeout
        };
    }

    private static JwtKeyRingDocument CreateKeyRing(
        string keyName,
        OpenBaoTransitKey key)
    {
        var versions = key.Versions
            .OrderBy(version => version.Version)
            .ToArray();

        var entries = versions
            .Select((version, index) =>
                new JwtKeyRingEntry
                {
                    KeyId = CreateKeyId(
                        keyName,
                        version.Version),
                    TransitKeyVersion =
                        version.Version,
                    PublicKeyPem =
                        version.PublicKeyPem,
                    CreatedAtUtc =
                        version.CreatedAtUtc,
                    RetiredAtUtc =
                        index < versions.Length - 1
                            ? versions[index + 1]
                                .CreatedAtUtc
                            : null
                })
            .ToArray();

        return new JwtKeyRingDocument
        {
            Version = KeyRingVersion,
            ActiveKeyId = CreateKeyId(
                keyName,
                key.LatestVersion),
            Keys = entries
        };
    }

    private static string CreateKeyId(
        string keyName,
        int version) =>
        $"{keyName}-v{version}";

    private static void ValidateKeyRing(
        JwtKeyRingDocument keyRing)
    {
        if (keyRing.Version != KeyRingVersion
            || keyRing.Keys.Count == 0
            || keyRing.Keys.All(entry =>
                entry.KeyId != keyRing.ActiveKeyId))
        {
            throw new InvalidOperationException(
                "The generated JWT public key ring is invalid.");
        }

        foreach (var entry in keyRing.Keys)
        {
            if (entry.TransitKeyVersion < 1)
            {
                throw new InvalidOperationException(
                    "The generated JWT public key ring contains an invalid Transit key version.");
            }

            using var rsa = RSA.Create();
            rsa.ImportFromPem(entry.PublicKeyPem);

            if (rsa.KeySize != 3_072)
            {
                throw new CryptographicException(
                    "OpenBao returned a JWT public key that is not RSA-3072.");
            }
        }
    }

    private static KeyStorePaths PreparePaths(
        string keyDirectory,
        string runtimeTokenFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            keyDirectory);

        var absoluteDirectory = Path.GetFullPath(
            keyDirectory);

        Directory.CreateDirectory(
            absoluteDirectory);

        RestrictDirectoryPermissions(
            absoluteDirectory);

        var absoluteTokenFile = Path.GetFullPath(
            runtimeTokenFile);

        if (!string.Equals(
                Path.GetDirectoryName(
                    absoluteTokenFile),
                absoluteDirectory,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OPENBAO_RUNTIME_TOKEN_FILE must be inside AUTH_KEY_DIRECTORY.");
        }

        return new KeyStorePaths(
            Path.Combine(
                absoluteDirectory,
                KeyRingFileName),
            absoluteTokenFile,
            Path.Combine(
                absoluteDirectory,
                LockFileName));
    }

    private static FileStream AcquireOperationLock(
        string lockFile)
    {
        try
        {
            return new FileStream(
                lockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Another JWT key operation is currently running.",
                exception);
        }
    }

    private static async Task WriteKeyRingAsync(
        string filePath,
        JwtKeyRingDocument keyRing,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            keyRing,
            JsonOptions);

        await WriteSensitiveFileAsync(
            filePath,
            $"{json}{Environment.NewLine}",
            cancellationToken);
    }

    private static async Task WriteSensitiveFileAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryFile =
            $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(
                temporaryFile,
                content,
                cancellationToken);

            RestrictFilePermissions(
                temporaryFile);

            File.Move(
                temporaryFile,
                filePath,
                overwrite: true);

            RestrictFilePermissions(filePath);
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    private static void RestrictDirectoryPermissions(
        string directoryPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directoryPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFilePermissions(
        string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                filePath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite);
        }
    }

    private sealed record KeyStorePaths(
        string KeyRingFile,
        string RuntimeTokenFile,
        string LockFile);

    private enum InitializationState
    {
        FreshStorage,
        MissingLocalArtifacts,
        Configured
    }
}
