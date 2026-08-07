using System.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public static class RsaKeyLoader
{
    private const int MinimumKeySizeBits =
        3_072;

    public static RSA LoadPublicKeyFromPem(
        string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            publicKeyPem);

        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(publicKeyPem);

            EnsureSecureKeySize(rsa);

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
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

}
