using System.Security.Cryptography;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Models;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private const int TokenSizeBytes = 32;
    private const int HashSizeBytes = 32;

    public RefreshTokenResult Create()
    {
        var tokenBytes =
            RandomNumberGenerator.GetBytes(TokenSizeBytes);

        try
        {
            var plainTextToken =
                Base64UrlEncode(tokenBytes);

            var tokenHash =
                SHA256.HashData(tokenBytes);

            return new RefreshTokenResult(
                plainTextToken,
                tokenHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    public byte[] Hash(string plainTextToken)
    {
        var tokenBytes =
            Base64UrlDecode(plainTextToken);

        try
        {
            if (tokenBytes.Length != TokenSizeBytes)
            {
                throw new FormatException(
                    "Refresh token has an invalid length.");
            }

            return SHA256.HashData(tokenBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    public bool Verify(
        string plainTextToken,
        byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);

        if (expectedHash.Length != HashSizeBytes)
        {
            return false;
        }

        byte[]? actualHash = null;

        try
        {
            actualHash = Hash(plainTextToken);

            return CryptographicOperations.FixedTimeEquals(
                actualHash,
                expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            if (actualHash is not null)
            {
                CryptographicOperations.ZeroMemory(
                    actualHash);
            }
        }
    }

    private static string Base64UrlEncode(
        ReadOnlySpan<byte> value)
    {
        return Convert
            .ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException(
                "Refresh token contains invalid Base64Url data.")
        };

        return Convert.FromBase64String(normalized);
    }
}