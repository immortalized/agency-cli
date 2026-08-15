using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Models;
using Konscious.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int Argon2Version = 19;

    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    // MemorySize is expressed in KiB.
    private const int MemorySizeKiB = 65_536;
    private const int Iterations = 3;
    private const int DegreeOfParallelism = 2;

    // Defensive parser limits. An attacker-controlled encoded hash must not
    // be able to force extreme memory or CPU consumption during login.
    private const int MaximumAcceptedMemorySizeKiB = 262_144;
    private const int MaximumAcceptedIterations = 10;
    private const int MaximumAcceptedParallelism = 16;

    public string Hash(string password)
    {
        ValidatePassword(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

        try
        {
            var hash = DeriveHash(
                password,
                salt,
                MemorySizeKiB,
                Iterations,
                DegreeOfParallelism,
                HashSizeBytes);

            return Encode(
                salt,
                hash,
                MemorySizeKiB,
                Iterations,
                DegreeOfParallelism);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public PasswordVerificationResult Verify(
        string password,
        string encodedHash)
    {
        if (string.IsNullOrEmpty(password) ||
            password.Length < PasswordPolicy.MinimumLength ||
            string.IsNullOrWhiteSpace(encodedHash))
        {
            return PasswordVerificationResult.Failed;
        }

        if (!TryParse(encodedHash, out var parsed))
        {
            return PasswordVerificationResult.Failed;
        }

        byte[]? actualHash = null;

        try
        {
            actualHash = DeriveHash(
                password,
                parsed.Salt,
                parsed.MemorySizeKiB,
                parsed.Iterations,
                parsed.DegreeOfParallelism,
                parsed.Hash.Length);

            if (!CryptographicOperations.FixedTimeEquals(
                    actualHash,
                    parsed.Hash))
            {
                return PasswordVerificationResult.Failed;
            }

            var rehashNeeded =
                parsed.MemorySizeKiB != MemorySizeKiB ||
                parsed.Iterations != Iterations ||
                parsed.DegreeOfParallelism != DegreeOfParallelism ||
                parsed.Hash.Length != HashSizeBytes ||
                parsed.Salt.Length != SaltSizeBytes;

            return rehashNeeded
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Success;
        }
        catch (CryptographicException)
        {
            return PasswordVerificationResult.Failed;
        }
        catch (ArgumentException)
        {
            return PasswordVerificationResult.Failed;
        }
        finally
        {
            if (actualHash is not null)
            {
                CryptographicOperations.ZeroMemory(actualHash);
            }

            CryptographicOperations.ZeroMemory(parsed.Salt);
            CryptographicOperations.ZeroMemory(parsed.Hash);
        }
    }

    private static byte[] DeriveHash(
        string password,
        byte[] salt,
        int memorySizeKiB,
        int iterations,
        int degreeOfParallelism,
        int hashSizeBytes)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = memorySizeKiB,
                Iterations = iterations,
                DegreeOfParallelism = degreeOfParallelism
            };

            return argon2.GetBytes(hashSizeBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static string Encode(
        byte[] salt,
        byte[] hash,
        int memorySizeKiB,
        int iterations,
        int degreeOfParallelism)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"$argon2id$v={Argon2Version}" +
            $"$m={memorySizeKiB},t={iterations},p={degreeOfParallelism}" +
            $"${Convert.ToBase64String(salt)}" +
            $"${Convert.ToBase64String(hash)}");
    }

    private static bool TryParse(
        string encodedHash,
        out ParsedArgon2Hash parsed)
    {
        parsed = ParsedArgon2Hash.Empty;

        var sections = encodedHash.Split(
            '$',
            StringSplitOptions.None);

        if (sections.Length != 6 ||
            sections[0].Length != 0 ||
            !string.Equals(
                sections[1],
                "argon2id",
                StringComparison.Ordinal) ||
            !string.Equals(
                sections[2],
                $"v={Argon2Version}",
                StringComparison.Ordinal))
        {
            return false;
        }

        var parameters = sections[3].Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (parameters.Length != 3)
        {
            return false;
        }

        int? memorySizeKiB = null;
        int? iterations = null;
        int? degreeOfParallelism = null;

        foreach (var parameter in parameters)
        {
            var pair = parameter.Split(
                '=',
                2,
                StringSplitOptions.TrimEntries);

            if (pair.Length != 2 ||
                !int.TryParse(
                    pair[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            switch (pair[0])
            {
                case "m":
                    memorySizeKiB = value;
                    break;

                case "t":
                    iterations = value;
                    break;

                case "p":
                    degreeOfParallelism = value;
                    break;

                default:
                    return false;
            }
        }

        if (memorySizeKiB is null ||
            iterations is null ||
            degreeOfParallelism is null ||
            memorySizeKiB <= 0 ||
            memorySizeKiB > MaximumAcceptedMemorySizeKiB ||
            iterations <= 0 ||
            iterations > MaximumAcceptedIterations ||
            degreeOfParallelism <= 0 ||
            degreeOfParallelism > MaximumAcceptedParallelism)
        {
            return false;
        }

        byte[] salt;
        byte[] hash;

        try
        {
            salt = Convert.FromBase64String(sections[4]);
            hash = Convert.FromBase64String(sections[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length < 16 ||
            salt.Length > 64 ||
            hash.Length < 16 ||
            hash.Length > 64)
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(hash);

            return false;
        }

        parsed = new ParsedArgon2Hash(
            memorySizeKiB.Value,
            iterations.Value,
            degreeOfParallelism.Value,
            salt,
            hash);

        return true;
    }

    private static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length < PasswordPolicy.MinimumLength)
        {
            throw new ArgumentException(
                $"Password must contain at least {PasswordPolicy.MinimumLength} characters.",
                nameof(password));
        }

        // Prevent unbounded request memory and hashing work.
        if (Encoding.UTF8.GetByteCount(password) > 1_024)
        {
            throw new ArgumentException(
                "Password cannot exceed 1024 UTF-8 bytes.",
                nameof(password));
        }
    }

    private sealed record ParsedArgon2Hash(
        int MemorySizeKiB,
        int Iterations,
        int DegreeOfParallelism,
        byte[] Salt,
        byte[] Hash)
    {
        public static ParsedArgon2Hash Empty { get; } = new(
            0,
            0,
            0,
            [],
            []);
    }
}
