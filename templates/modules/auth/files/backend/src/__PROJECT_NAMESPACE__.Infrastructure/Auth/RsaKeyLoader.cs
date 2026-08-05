using System.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public static class RsaKeyLoader
{
    private const int MinimumKeySizeBits = 3_072;

    public static RSA LoadPrivateKey(string privateKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            privateKeyPem);

        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(NormalizePem(privateKeyPem));

            EnsureSecureKeySize(rsa);

            if (!HasPrivateKey(rsa))
            {
                throw new InvalidOperationException(
                    "The configured JWT private key does not contain private key material.");
            }

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public static RSA LoadPublicKey(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            publicKeyPem);

        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(NormalizePem(publicKeyPem));

            EnsureSecureKeySize(rsa);

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static void EnsureSecureKeySize(RSA rsa)
    {
        if (rsa.KeySize < MinimumKeySizeBits)
        {
            throw new InvalidOperationException(
                $"JWT RSA keys must be at least {MinimumKeySizeBits} bits.");
        }
    }

    private static bool HasPrivateKey(RSA rsa)
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

    private static string NormalizePem(string pem)
    {
        return pem
            .Replace("\\r", string.Empty)
            .Replace("\\n", "\n")
            .Trim();
    }
}