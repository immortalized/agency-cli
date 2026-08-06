using System.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public static class RsaKeyLoader
{
    private const int MinimumKeySizeBits = 3_072;

    public static RSA LoadPrivateKeyFromFile(
        string privateKeyFile)
    {
        var pem = ReadPemFile(
            privateKeyFile,
            "JWT private key");

        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(pem);

            EnsureSecureKeySize(rsa);

            if (!HasPrivateKey(rsa))
            {
                throw new InvalidOperationException(
                    "The configured JWT private key file does not contain private key material.");
            }

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public static RSA LoadPublicKeyFromFile(
        string publicKeyFile)
    {
        var pem = ReadPemFile(
            publicKeyFile,
            "JWT public key");

        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(pem);

            EnsureSecureKeySize(rsa);

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static string ReadPemFile(
        string filePath,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath);

        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new InvalidOperationException(
                $"{description} path must be absolute.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"{description} file was not found.",
                filePath);
        }

        string pem;

        try
        {
            pem = File.ReadAllText(filePath);
        }
        catch (Exception exception)
            when (
                exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{description} file could not be read.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                $"{description} file is empty.");
        }

        return pem.Trim();
    }

    private static void EnsureSecureKeySize(
        RSA rsa)
    {
        if (rsa.KeySize < MinimumKeySizeBits)
        {
            throw new InvalidOperationException(
                $"JWT RSA keys must be at least {MinimumKeySizeBits} bits.");
        }
    }

    private static bool HasPrivateKey(
        RSA rsa)
    {
        try
        {
            var parameters =
                rsa.ExportParameters(
                    includePrivateParameters: true);

            return parameters.D is
            {
                Length: > 0
            };
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}