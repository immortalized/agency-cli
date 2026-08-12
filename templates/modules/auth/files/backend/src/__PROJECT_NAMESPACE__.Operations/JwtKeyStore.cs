using System.Security.Cryptography;
using System.Text.Json;

namespace __PROJECT_NAMESPACE__.Operations;

public static class JwtKeyStore
{
    private const int KeyRingVersion = 2;

    private const string KeyRingFileName =
        "auth-jwt-key-ring.json";

    private const string LockFileName =
        ".auth-key-operation.lock";

    private const string CompletionMarkerFileName =
        AuthProvisioningMarkers.CompletionFileName;

    private const string BootstrapRetirementMarkerFileName =
        AuthProvisioningMarkers
            .BootstrapRetirementPendingFileName;

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
            string? bootstrapToken,
            bool revokeBootstrapToken,
            CancellationToken cancellationToken = default)
    {
        var options = OpenBaoToolOptions
            .FromEnvironment(bootstrapToken);

        var paths = PreparePaths(
            keyDirectory,
            options.RuntimeTokenFile,
            options.MigratorTokenFile,
            options.JwtRotationTokenFile,
            options.DatabaseRotationTokenFile,
            options.ProvisioningTokenFile);

        using var operationLock =
            AcquireOperationLock(paths.LockFile);

        if (revokeBootstrapToken)
        {
            bootstrapToken = await CreateResumableProvisioningTokenAsync(
                options,
                paths,
                cancellationToken);
            options = OpenBaoToolOptions.FromEnvironment(bootstrapToken);
        }

        using var httpClient = CreateHttpClient(options);
        var client = new OpenBaoTransitAdminClient(httpClient, options);

        await client.EnsureTransitEnabledAsync(
            cancellationToken);

        var key = await client.ReadKeyAsync(
            cancellationToken);

        var initializationState =
            GetInitializationState(
            paths,
            key);

        if (initializationState ==
            InitializationState.FreshStorage)
        {
            DeleteStaleLocalArtifacts(paths);

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

        var missingDatabaseConfiguration =
            await databaseClient.GetMissingConfigurationAsync(
                cancellationToken);

        if (missingDatabaseConfiguration.Count > 0)
        {
            Console.WriteLine(
                $"Resuming database provisioning; missing: {string.Join(", ", missingDatabaseConfiguration)}.");
            var managementCredential =
                await PostgresRoleBootstrapper
                    .BootstrapAsync(
                        databaseOptions,
                        cancellationToken);

            await databaseClient.ConfigureAsync(
                managementCredential,
                cancellationToken);

            await databaseClient.ReadRuntimeCredentialAsync(
                cancellationToken);

            await databaseClient.ReadMigratorCredentialAsync(
                cancellationToken);
        }
        else
        {
            await databaseClient.ReadRuntimeCredentialAsync(
                cancellationToken);
            await databaseClient.ReadMigratorCredentialAsync(
                cancellationToken);
        }

        await client.WriteRuntimePolicyAsync(
            cancellationToken);

        await client.WriteMigratorPolicyAsync(
            cancellationToken);

        var keyRing = CreateKeyRing(
            options.KeyName,
            configuredKey);

        ValidateKeyRing(keyRing);

        await EnsureTokenFileAsync(
            paths.RuntimeTokenFile,
            client.CreateRuntimeTokenAsync,
            cancellationToken);
        await EnsureTokenFileAsync(
            paths.MigratorTokenFile,
            client.CreateMigratorTokenAsync,
            cancellationToken);
        await EnsureTokenFileAsync(
            paths.JwtRotationTokenFile,
            client.CreateJwtRotationTokenAsync,
            cancellationToken);
        await EnsureTokenFileAsync(
            paths.DatabaseRotationTokenFile,
            client.CreateDatabaseRotationTokenAsync,
            cancellationToken);

        await WriteKeyRingAsync(
            paths.KeyRingFile,
            keyRing,
            cancellationToken);

        await OpenBaoRuntimePolicyVerifier
            .VerifyAsync(cancellationToken);

        var provisioningWasAlreadyComplete = File.Exists(
            paths.CompletionMarkerFile);
        if (!provisioningWasAlreadyComplete
            && OperationsEnvironment.FromEnvironment().IsProduction
            && !databaseOptions.RetireBootstrapCredential)
        {
            throw new InvalidOperationException(
                "Production auth provisioning is incomplete and must be resumed with the bootstrap Compose overlay so the PostgreSQL bootstrap login can be retired.");
        }

        if (databaseOptions.RetireBootstrapCredential)
        {
            var resumingBootstrapRetirement = File.Exists(
                paths.BootstrapRetirementMarkerFile);
            if (!resumingBootstrapRetirement)
            {
                await WriteSensitiveFileAsync(
                    paths.BootstrapRetirementMarkerFile,
                    $"version=1{Environment.NewLine}",
                    cancellationToken);
            }

            await PostgresRoleBootstrapper
                .RetireBootstrapCredentialAsync(
                    databaseOptions,
                    resumingBootstrapRetirement,
                    cancellationToken);
        }

        await WriteSensitiveFileAsync(
            paths.CompletionMarkerFile,
            $"version=1{Environment.NewLine}",
            cancellationToken);

        await CompleteResumableProvisioningAsync(
            client,
            paths.ProvisioningTokenFile,
            cancellationToken);
        File.Delete(paths.BootstrapRetirementMarkerFile);

        return new JwtKeyOperationResult(
            keyRing.ActiveKeyId,
            keyRing.Keys.Count);
    }

    public static AuthProvisioningDiagnostic
        GetProvisioningDiagnostic(
            string keyDirectory,
            OpenBaoToolOptions options)
    {
        var paths = PreparePaths(
            keyDirectory,
            options.RuntimeTokenFile,
            options.MigratorTokenFile,
            options.JwtRotationTokenFile,
            options.DatabaseRotationTokenFile,
            options.ProvisioningTokenFile);
        var missing = new List<string>();

        AddMissing(paths.KeyRingFile, "JWT public key ring", missing);
        AddMissing(paths.RuntimeTokenFile, "runtime token", missing);
        AddMissing(paths.MigratorTokenFile, "migrator token", missing);
        AddMissing(paths.JwtRotationTokenFile, "JWT rotation token", missing);
        AddMissing(paths.DatabaseRotationTokenFile, "database rotation token", missing);

        if (File.Exists(paths.CompletionMarkerFile) && missing.Count == 0)
        {
            return new AuthProvisioningDiagnostic(
                true,
                "Complete.");
        }

        var details = missing.Count == 0
            ? "the completion marker is missing"
            : $"missing {string.Join(", ", missing)}";
        var resume = File.Exists(paths.ProvisioningTokenFile)
            ? " A short-lived provisioning credential is available; run 'auth init' to resume."
            : " No resumable provisioning credential was persisted; recovery requires a temporary administrative token obtained through OpenBao's authenticated root-generation workflow, or recreation of this deployment's OpenBao and database volumes.";
        return new AuthProvisioningDiagnostic(
            false,
            $"Incomplete: {details}.{resume}");
    }

    private static async Task<string>
        CreateResumableProvisioningTokenAsync(
            OpenBaoToolOptions rootOptions,
            KeyStorePaths paths,
            CancellationToken cancellationToken)
    {
        using var rootHttpClient = CreateHttpClient(rootOptions);
        var rootClient = new OpenBaoTransitAdminClient(
            rootHttpClient,
            rootOptions);
        await rootClient.WriteProvisioningPolicyAsync(
            cancellationToken);
        await rootClient.WriteProvisioningTokenRoleAsync(
            cancellationToken);
        var provisioningToken =
            await rootClient.CreateProvisioningTokenAsync(
                cancellationToken);

        await WriteProvisioningTokenUntilSuccessfulAsync(
            paths.ProvisioningTokenFile,
            provisioningToken);

        await rootClient.RevokeBootstrapTokenAsync(
            cancellationToken);
        Console.WriteLine(
            "The initial root token was replaced by a short-lived, least-privilege provisioning credential. Interrupted provisioning can now be resumed with 'auth init'.");
        return provisioningToken;
    }

    private static async Task
        WriteProvisioningTokenUntilSuccessfulAsync(
            string filePath,
            string token)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await WriteSensitiveFileAsync(
                    filePath,
                    $"{token}{Environment.NewLine}",
                    CancellationToken.None);
                return;
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(
                    $"The resumable provisioning token could not be persisted (attempt {attempt}): {exception.Message} This process still holds the only usable bootstrap credential and will retry.");
                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);
            }
        }
    }

    private static async Task EnsureTokenFileAsync(
        string filePath,
        Func<CancellationToken, Task<string>> createToken,
        CancellationToken cancellationToken)
    {
        if (File.Exists(filePath)
            && new FileInfo(filePath).Length > 0)
        {
            return;
        }

        var token = await createToken(cancellationToken);
        await WriteSensitiveFileAsync(
            filePath,
            $"{token}{Environment.NewLine}",
            cancellationToken);
    }

    private static async Task CompleteResumableProvisioningAsync(
        OpenBaoTransitAdminClient client,
        string provisioningTokenFile,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(provisioningTokenFile))
        {
            return;
        }

        await client.RevokeBootstrapTokenAsync(cancellationToken);
        File.Delete(provisioningTokenFile);
    }

    private static void DeleteStaleLocalArtifacts(KeyStorePaths paths)
    {
        File.Delete(paths.KeyRingFile);
        File.Delete(paths.RuntimeTokenFile);
        File.Delete(paths.MigratorTokenFile);
        File.Delete(paths.JwtRotationTokenFile);
        File.Delete(paths.DatabaseRotationTokenFile);
        File.Delete(paths.CompletionMarkerFile);
    }

    private static void AddMissing(
        string path,
        string description,
        ICollection<string> missing)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            missing.Add(description);
        }
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

        var migratorTokenExists = File.Exists(
            paths.MigratorTokenFile);

        var jwtRotationTokenExists = File.Exists(
            paths.JwtRotationTokenFile);

        var databaseRotationTokenExists = File.Exists(
            paths.DatabaseRotationTokenFile);

        if (keyRingExists && runtimeTokenExists && migratorTokenExists
            && jwtRotationTokenExists && databaseRotationTokenExists
            && File.Exists(paths.CompletionMarkerFile))
        {
            return InitializationState.Configured;
        }

        return InitializationState.MissingLocalArtifacts;
    }

    public static async Task<JwtKeyOperationResult>
        RotateAsync(
            string keyDirectory,
            string? bootstrapToken,
            CancellationToken cancellationToken = default)
    {
        var options = OpenBaoToolOptions
            .FromEnvironment(bootstrapToken);

        var paths = PreparePaths(
            keyDirectory,
            options.RuntimeTokenFile,
            options.MigratorTokenFile,
            options.JwtRotationTokenFile,
            options.DatabaseRotationTokenFile,
            options.ProvisioningTokenFile);

        using var operationLock =
            AcquireOperationLock(paths.LockFile);

        if (!File.Exists(paths.KeyRingFile))
        {
            throw new InvalidOperationException(
                "JWT signing has not been initialized. Run 'auth init' first.");
        }

        using var httpClient = CreateHttpClient(options);
        var client = new OpenBaoTransitAdminClient(
            httpClient,
            options);

        var currentKey = await client.ReadKeyAsync(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The OpenBao Transit signing key does not exist. Run 'auth init' to bootstrap the instance.");

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
        string runtimeTokenFile,
        string migratorTokenFile,
        string jwtRotationTokenFile,
        string databaseRotationTokenFile,
        string provisioningTokenFile)
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

        var absoluteMigratorTokenFile = Path.GetFullPath(
            migratorTokenFile);
        var migratorDirectory = Path.GetDirectoryName(
            absoluteMigratorTokenFile)
            ?? throw new InvalidOperationException(
                "OPENBAO_MIGRATOR_TOKEN_FILE must have a parent directory.");
        Directory.CreateDirectory(migratorDirectory);
        RestrictDirectoryPermissions(migratorDirectory);

        var absoluteProvisioningTokenFile = Path.GetFullPath(
            provisioningTokenFile);
        var provisioningDirectory = Path.GetDirectoryName(
            absoluteProvisioningTokenFile)
            ?? throw new InvalidOperationException(
                "OPENBAO_PROVISIONING_TOKEN_FILE must have a parent directory.");
        Directory.CreateDirectory(provisioningDirectory);
        RestrictDirectoryPermissions(provisioningDirectory);

        return new KeyStorePaths(
            Path.Combine(
                absoluteDirectory,
                KeyRingFileName),
            absoluteTokenFile,
            absoluteMigratorTokenFile,
            Path.GetFullPath(jwtRotationTokenFile),
            Path.GetFullPath(databaseRotationTokenFile),
            absoluteProvisioningTokenFile,
            Path.Combine(
                absoluteDirectory,
                CompletionMarkerFileName),
            Path.Combine(
                absoluteDirectory,
                BootstrapRetirementMarkerFileName),
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
        string MigratorTokenFile,
        string JwtRotationTokenFile,
        string DatabaseRotationTokenFile,
        string ProvisioningTokenFile,
        string CompletionMarkerFile,
        string BootstrapRetirementMarkerFile,
        string LockFile);

    private enum InitializationState
    {
        FreshStorage,
        MissingLocalArtifacts,
        Configured
    }
}
