using System.Security.Cryptography;
using System.Text.Json;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class JwtKeyStore
{
    private const int KeySizeBits = 3_072;
    private const int KeyRingVersion = 1;

    private const string PrivateKeyFileName =
        "auth-jwt-private-key.pem";

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
            CancellationToken cancellationToken =
                default)
    {
        var paths = PreparePaths(keyDirectory);

        using var operationLock =
            AcquireOperationLock(paths.LockFile);

        EnsureFileDoesNotExist(
            paths.PrivateKeyFile);

        EnsureFileDoesNotExist(
            paths.KeyRingFile);

        using var rsa = RSA.Create(KeySizeBits);

        var keyId = GenerateKeyId();
        var createdAtUtc = DateTimeOffset.UtcNow;

        var privateKeyPem =
            rsa.ExportPkcs8PrivateKeyPem();

        var publicKeyPem =
            rsa.ExportSubjectPublicKeyInfoPem();

        var keyRing = new JwtKeyRingDocument
        {
            Version = KeyRingVersion,
            ActiveKeyId = keyId,
            Keys =
            [
                new JwtKeyRingEntry
                {
                    KeyId = keyId,
                    PublicKeyPem = publicKeyPem,
                    CreatedAtUtc = createdAtUtc,
                    RetiredAtUtc = null
                }
            ]
        };

        await WriteInitialFilesAsync(
            paths,
            privateKeyPem,
            keyRing,
            cancellationToken);

        ValidateKeyStore(
            paths.PrivateKeyFile,
            paths.KeyRingFile);

        return new JwtKeyOperationResult(
            keyId,
            keyRing.Keys.Count);
    }

    public static async Task<JwtKeyOperationResult>
        RotateAsync(
            string keyDirectory,
            CancellationToken cancellationToken =
                default)
    {
        var paths = PreparePaths(keyDirectory);

        using var operationLock =
            AcquireOperationLock(paths.LockFile);

        ValidateKeyStore(
            paths.PrivateKeyFile,
            paths.KeyRingFile);

        var existingKeyRing =
            await ReadKeyRingAsync(
                paths.KeyRingFile,
                cancellationToken);

        using var rsa = RSA.Create(KeySizeBits);

        var nowUtc = DateTimeOffset.UtcNow;
        var newKeyId = GenerateKeyId();

        var newPrivateKeyPem =
            rsa.ExportPkcs8PrivateKeyPem();

        var newPublicKeyPem =
            rsa.ExportSubjectPublicKeyInfoPem();

        var updatedEntries = existingKeyRing.Keys
            .Select(entry =>
                entry.KeyId ==
                existingKeyRing.ActiveKeyId
                    ? new JwtKeyRingEntry
                    {
                        KeyId = entry.KeyId,
                        PublicKeyPem =
                            entry.PublicKeyPem,
                        CreatedAtUtc =
                            entry.CreatedAtUtc,
                        RetiredAtUtc =
                            entry.RetiredAtUtc
                            ?? nowUtc
                    }
                    : entry)
            .Append(
                new JwtKeyRingEntry
                {
                    KeyId = newKeyId,
                    PublicKeyPem =
                        newPublicKeyPem,
                    CreatedAtUtc = nowUtc,
                    RetiredAtUtc = null
                })
            .ToArray();

        var updatedKeyRing =
            new JwtKeyRingDocument
            {
                Version = KeyRingVersion,
                ActiveKeyId = newKeyId,
                Keys = updatedEntries
            };

        await ReplaceFilesWithRollbackAsync(
            paths,
            newPrivateKeyPem,
            updatedKeyRing,
            cancellationToken);

        ValidateKeyStore(
            paths.PrivateKeyFile,
            paths.KeyRingFile);

        return new JwtKeyOperationResult(
            newKeyId,
            updatedEntries.Length);
    }

    private static KeyStorePaths PreparePaths(
        string keyDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            keyDirectory);

        var absoluteDirectory =
            Path.GetFullPath(keyDirectory);

        Directory.CreateDirectory(
            absoluteDirectory);

        RestrictDirectoryPermissions(
            absoluteDirectory);

        return new KeyStorePaths(
            Path.Combine(
                absoluteDirectory,
                PrivateKeyFileName),

            Path.Combine(
                absoluteDirectory,
                KeyRingFileName),

            Path.Combine(
                absoluteDirectory,
                LockFileName));
    }

    private static FileStream AcquireOperationLock(
        string lockFile)
    {
        try
        {
            /*
             * The lock file is intentionally allowed to
             * remain on disk.
             *
             * Mutual exclusion comes from FileShare.None,
             * not from the existence of the file.
             *
             * If the process or container stops, the OS
             * releases the file handle automatically.
             */
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
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                "The JWT key operation lock file could not be opened.",
                exception);
        }
    }

    private static void EnsureFileDoesNotExist(
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Authentication key initialization cannot continue because '{Path.GetFileName(filePath)}' already exists.");
    }

    private static async Task WriteInitialFilesAsync(
        KeyStorePaths paths,
        string privateKeyPem,
        JwtKeyRingDocument keyRing,
        CancellationToken cancellationToken)
    {
        var createdFiles = new List<string>();

        try
        {
            await WriteNewTextFileAsync(
                paths.PrivateKeyFile,
                privateKeyPem,
                cancellationToken);

            createdFiles.Add(
                paths.PrivateKeyFile);

            RestrictPrivateFilePermissions(
                paths.PrivateKeyFile);

            await WriteNewKeyRingAsync(
                paths.KeyRingFile,
                keyRing,
                cancellationToken);

            createdFiles.Add(
                paths.KeyRingFile);

            RestrictPrivateFilePermissions(
                paths.KeyRingFile);
        }
        catch
        {
            DeleteFiles(createdFiles);
            throw;
        }
    }

    private static async Task
        ReplaceFilesWithRollbackAsync(
            KeyStorePaths paths,
            string privateKeyPem,
            JwtKeyRingDocument keyRing,
            CancellationToken cancellationToken)
    {
        var operationId =
            Guid.NewGuid().ToString("N");

        var temporaryPrivateKeyFile =
            $"{paths.PrivateKeyFile}.{operationId}.tmp";

        var temporaryKeyRingFile =
            $"{paths.KeyRingFile}.{operationId}.tmp";

        var backupPrivateKeyFile =
            $"{paths.PrivateKeyFile}.{operationId}.bak";

        var backupKeyRingFile =
            $"{paths.KeyRingFile}.{operationId}.bak";

        var privateKeyReplaced = false;
        var keyRingReplaced = false;

        try
        {
            await WriteNewTextFileAsync(
                temporaryPrivateKeyFile,
                privateKeyPem,
                cancellationToken);

            RestrictPrivateFilePermissions(
                temporaryPrivateKeyFile);

            await WriteNewKeyRingAsync(
                temporaryKeyRingFile,
                keyRing,
                cancellationToken);

            RestrictPrivateFilePermissions(
                temporaryKeyRingFile);

            ValidateKeyStore(
                temporaryPrivateKeyFile,
                temporaryKeyRingFile);

            File.Copy(
                paths.PrivateKeyFile,
                backupPrivateKeyFile,
                overwrite: false);

            File.Copy(
                paths.KeyRingFile,
                backupKeyRingFile,
                overwrite: false);

            RestrictPrivateFilePermissions(
                backupPrivateKeyFile);

            RestrictPrivateFilePermissions(
                backupKeyRingFile);

            File.Move(
                temporaryPrivateKeyFile,
                paths.PrivateKeyFile,
                overwrite: true);

            privateKeyReplaced = true;

            File.Move(
                temporaryKeyRingFile,
                paths.KeyRingFile,
                overwrite: true);

            keyRingReplaced = true;

            RestrictPrivateFilePermissions(
                paths.PrivateKeyFile);

            RestrictPrivateFilePermissions(
                paths.KeyRingFile);
        }
        catch
        {
            if (privateKeyReplaced)
            {
                RestoreBackup(
                    backupPrivateKeyFile,
                    paths.PrivateKeyFile);
            }

            if (keyRingReplaced)
            {
                RestoreBackup(
                    backupKeyRingFile,
                    paths.KeyRingFile);
            }

            throw;
        }
        finally
        {
            DeleteFiles(
            [
                temporaryPrivateKeyFile,
                temporaryKeyRingFile,
                backupPrivateKeyFile,
                backupKeyRingFile
            ]);
        }
    }

    private static async Task WriteNewKeyRingAsync(
        string filePath,
        JwtKeyRingDocument keyRing,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            keyRing,
            JsonOptions);

        await WriteNewTextFileAsync(
            filePath,
            $"{json}{Environment.NewLine}",
            cancellationToken);
    }

    private static async Task
        WriteNewTextFileAsync(
            string filePath,
            string content,
            CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4_096,
                options:
                    FileOptions.Asynchronous
                    | FileOptions.WriteThrough);

        await using var writer =
            new StreamWriter(
                stream,
                leaveOpen: true);

        await writer.WriteAsync(
            content.AsMemory(),
            cancellationToken);

        await writer.FlushAsync(
            cancellationToken);

        stream.Flush(
            flushToDisk: true);
    }

    private static async Task<JwtKeyRingDocument>
        ReadKeyRingAsync(
            string keyRingFile,
            CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                keyRingFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var keyRing =
            await JsonSerializer.DeserializeAsync<
                JwtKeyRingDocument>(
                stream,
                JsonOptions,
                cancellationToken);

        return keyRing
            ?? throw new InvalidOperationException(
                "JWT key ring contains invalid JSON.");
    }

    private static void ValidateKeyStore(
        string privateKeyFile,
        string keyRingFile)
    {
        if (!File.Exists(privateKeyFile))
        {
            throw new FileNotFoundException(
                "JWT private key file was not found.",
                privateKeyFile);
        }

        if (!File.Exists(keyRingFile))
        {
            throw new FileNotFoundException(
                "JWT key ring file was not found.",
                keyRingFile);
        }

        var privateKeyPem =
            File.ReadAllText(privateKeyFile);

        var keyRingJson =
            File.ReadAllText(keyRingFile);

        JwtKeyRingDocument keyRing;

        try
        {
            keyRing =
                JsonSerializer.Deserialize<
                    JwtKeyRingDocument>(
                    keyRingJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "JWT key ring contains invalid JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "JWT key ring contains invalid JSON.",
                exception);
        }

        if (keyRing.Version != KeyRingVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported JWT key ring version '{keyRing.Version}'.");
        }

        if (string.IsNullOrWhiteSpace(
                keyRing.ActiveKeyId))
        {
            throw new InvalidOperationException(
                "JWT key ring has no active key id.");
        }

        if (keyRing.Keys.Count == 0)
        {
            throw new InvalidOperationException(
                "JWT key ring does not contain any validation keys.");
        }

        if (
            keyRing.Keys
                .Select(entry => entry.KeyId)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != keyRing.Keys.Count)
        {
            throw new InvalidOperationException(
                "JWT key ring contains duplicate key ids.");
        }

        foreach (var entry in keyRing.Keys)
        {
            ValidateKeyRingEntry(entry);
        }

        var activeEntry = keyRing.Keys
            .SingleOrDefault(entry =>
                entry.KeyId ==
                keyRing.ActiveKeyId)
            ?? throw new InvalidOperationException(
                "JWT key ring active key does not exist.");

        if (activeEntry.RetiredAtUtc is not null)
        {
            throw new InvalidOperationException(
                "JWT key ring active key cannot be retired.");
        }

        using var privateRsa = RSA.Create();
        privateRsa.ImportFromPem(privateKeyPem);

        using var publicRsa = RSA.Create();
        publicRsa.ImportFromPem(
            activeEntry.PublicKeyPem);

        if (
            privateRsa.KeySize != KeySizeBits
            || publicRsa.KeySize != KeySizeBits)
        {
            throw new CryptographicException(
                $"JWT RSA keys must be {KeySizeBits} bits.");
        }

        EnsurePrivateKeyMaterial(privateRsa);

        var privatePublic =
            privateRsa.ExportParameters(
                includePrivateParameters: false);

        var publicParameters =
            publicRsa.ExportParameters(
                includePrivateParameters: false);

        if (
            privatePublic.Modulus is null
            || publicParameters.Modulus is null
            || privatePublic.Exponent is null
            || publicParameters.Exponent is null
            || !privatePublic.Modulus
                .AsSpan()
                .SequenceEqual(
                    publicParameters.Modulus)
            || !privatePublic.Exponent
                .AsSpan()
                .SequenceEqual(
                    publicParameters.Exponent))
        {
            throw new CryptographicException(
                "JWT private key does not match the active public key.");
        }
    }

    private static void ValidateKeyRingEntry(
        JwtKeyRingEntry entry)
    {
        if (string.IsNullOrWhiteSpace(
                entry.KeyId))
        {
            throw new InvalidOperationException(
                "JWT key ring contains an empty key id.");
        }

        if (entry.KeyId.Length > 128)
        {
            throw new InvalidOperationException(
                $"JWT key id '{entry.KeyId}' exceeds 128 characters.");
        }

        if (string.IsNullOrWhiteSpace(
                entry.PublicKeyPem))
        {
            throw new InvalidOperationException(
                $"JWT key '{entry.KeyId}' has no public key.");
        }

        if (
            entry.RetiredAtUtc is not null
            && entry.RetiredAtUtc
                < entry.CreatedAtUtc)
        {
            throw new InvalidOperationException(
                $"JWT key '{entry.KeyId}' was retired before it was created.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(entry.PublicKeyPem);

        if (rsa.KeySize != KeySizeBits)
        {
            throw new CryptographicException(
                $"JWT key '{entry.KeyId}' must be {KeySizeBits} bits.");
        }
    }

    private static void EnsurePrivateKeyMaterial(
        RSA rsa)
    {
        try
        {
            var parameters =
                rsa.ExportParameters(
                    includePrivateParameters: true);

            if (parameters.D is not
                {
                    Length: > 0
                })
            {
                throw new CryptographicException(
                    "JWT private key does not contain private key material.");
            }
        }
        catch (CryptographicException)
        {
            throw;
        }
    }

    private static void RestoreBackup(
        string backupFile,
        string destinationFile)
    {
        if (!File.Exists(backupFile))
        {
            return;
        }

        try
        {
            File.Copy(
                backupFile,
                destinationFile,
                overwrite: true);

            RestrictPrivateFilePermissions(
                destinationFile);
        }
        catch
        {
            // Preserve the original rotation error.
        }
    }

    private static void DeleteFiles(
        IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Preserve the original operation error.
            }
        }
    }

    private static string GenerateKeyId()
    {
        var timestamp =
            DateTimeOffset.UtcNow.ToString(
                "yyyyMMddHHmmss");

        var randomSuffix =
            Convert.ToHexString(
                    RandomNumberGenerator
                        .GetBytes(8))
                .ToLowerInvariant();

        return
            $"key-{timestamp}-{randomSuffix}";
    }

    private static void
        RestrictDirectoryPermissions(
            string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            directoryPath,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute);
    }

    private static void
        RestrictPrivateFilePermissions(
            string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            filePath,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite);
    }

    private sealed record KeyStorePaths(
        string PrivateKeyFile,
        string KeyRingFile,
        string LockFile);
}