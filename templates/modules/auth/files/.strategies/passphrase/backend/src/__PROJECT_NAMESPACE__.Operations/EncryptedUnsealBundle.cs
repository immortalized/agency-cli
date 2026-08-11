using System.Security.Cryptography;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Operations;

public static class EncryptedUnsealBundle
{
    private const int Version = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int KeySize = 32;
    private const int TagSize = 16;
    private const int MemoryKiB = 65_536;
    private const int Iterations = 3;
    private const int Parallelism = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task WriteAsync(
        string path,
        ReadOnlyMemory<byte> share,
        ReadOnlyMemory<byte> passphrase,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(passphrase.Span, salt);
        var ciphertext = new byte[share.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, share.Span, ciphertext, tag, AssociatedData);
            var document = new BundleDocument(
                Version,
                "argon2id",
                MemoryKiB,
                Iterations,
                Parallelism,
                Convert.ToBase64String(salt),
                "aes-256-gcm",
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                Restrict(temporary);
                File.Move(temporary, path, overwrite);
                Restrict(path);
            }
            finally
            {
                File.Delete(temporary);
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public static async Task<byte[]> ReadAsync(
        string path,
        ReadOnlyMemory<byte> passphrase,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            var document = JsonSerializer.Deserialize<BundleDocument>(
                bytes,
                JsonOptions)
                ?? throw new InvalidOperationException("The encrypted unseal bundle is empty.");
            Validate(document);
            var salt = Convert.FromBase64String(document.Salt);
            var nonce = Convert.FromBase64String(document.Nonce);
            var ciphertext = Convert.FromBase64String(document.Ciphertext);
            var tag = Convert.FromBase64String(document.Tag);
            var key = DeriveKey(passphrase.Span, salt);
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
                return plaintext;
            }
            catch (AuthenticationTagMismatchException exception)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new InvalidOperationException(
                    "The passphrase is incorrect or the encrypted unseal bundle was modified.",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The encrypted unseal bundle is malformed.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> passphrase, byte[] salt)
    {
        var input = passphrase.ToArray();
        try
        {
            using var argon2 = new Argon2id(input)
            {
                Salt = salt,
                MemorySize = MemoryKiB,
                Iterations = Iterations,
                DegreeOfParallelism = Parallelism
            };
            return argon2.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static ReadOnlySpan<byte> AssociatedData =>
        "agency-openbao-unseal-bundle-v1"u8;

    private static void Validate(BundleDocument value)
    {
        if (value.Version != Version
            || value.Kdf != "argon2id"
            || value.MemoryKiB != MemoryKiB
            || value.Iterations != Iterations
            || value.Parallelism != Parallelism
            || value.Cipher != "aes-256-gcm")
        {
            throw new InvalidOperationException("The encrypted unseal bundle format is unsupported.");
        }
    }

    private static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed record BundleDocument(
        int Version,
        string Kdf,
        int MemoryKiB,
        int Iterations,
        int Parallelism,
        string Salt,
        string Cipher,
        string Nonce,
        string Ciphertext,
        string Tag);
}
