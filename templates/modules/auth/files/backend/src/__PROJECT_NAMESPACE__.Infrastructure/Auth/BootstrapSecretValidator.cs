using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class BootstrapSecretValidator(
    IOptions<AuthOptions> options)
{
    private readonly byte[] _expectedHash =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                options.Value.BootstrapSecret));

    public bool IsValid(string? suppliedSecret)
    {
        if (string.IsNullOrEmpty(suppliedSecret))
        {
            return false;
        }

        var suppliedBytes =
            Encoding.UTF8.GetBytes(suppliedSecret);

        byte[]? suppliedHash = null;

        try
        {
            suppliedHash = SHA256.HashData(suppliedBytes);

            return CryptographicOperations.FixedTimeEquals(
                suppliedHash,
                _expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                suppliedBytes);

            if (suppliedHash is not null)
            {
                CryptographicOperations.ZeroMemory(
                    suppliedHash);
            }
        }
    }
}