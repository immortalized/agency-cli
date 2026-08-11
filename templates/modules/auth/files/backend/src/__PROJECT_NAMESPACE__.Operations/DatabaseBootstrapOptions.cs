using System.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Operations;

public sealed record DatabaseBootstrapOptions(
    string Host,
    int Port,
    string DatabaseName,
    string BootstrapUsername,
    string? BootstrapPasswordFile,
    string MigratorUsername,
    string? MigratorPasswordFile,
    string OpenBaoManagerUsername,
    string RuntimeUsername,
    string LegacyUsername,
    string SecretsMount,
    string ConnectionName,
    string StaticRoleName,
    string MigratorStaticRoleName,
    string RotationPeriod)
{
    public static DatabaseBootstrapOptions
        FromEnvironment()
    {
        var portValue = Required(
            "DATABASE_BOOTSTRAP_PORT");

        if (!int.TryParse(portValue, out var port)
            || port is < 1 or > 65_535)
        {
            throw new InvalidOperationException(
                "DATABASE_BOOTSTRAP_PORT must be between 1 and 65535.");
        }

        return new DatabaseBootstrapOptions(
            Required("DATABASE_BOOTSTRAP_HOST"),
            port,
            Required("DATABASE_BOOTSTRAP_NAME"),
            RequiredRole("DATABASE_BOOTSTRAP_USERNAME"),
            OptionalPath("DATABASE_BOOTSTRAP_PASSWORD_FILE"),
            RequiredRole("DATABASE_MIGRATOR_USERNAME"),
            OptionalPath("DATABASE_MIGRATOR_PASSWORD_FILE"),
            RequiredRole(
                "DATABASE_OPENBAO_MANAGER_USERNAME"),
            RequiredRole("DATABASE_RUNTIME_USERNAME"),
            RequiredRole("DATABASE_LEGACY_USERNAME"),
            RequiredPathSegment(
                "OPENBAO_DATABASE_MOUNT"),
            RequiredPathSegment(
                "OPENBAO_DATABASE_CONNECTION_NAME"),
            RequiredPathSegment(
                "OPENBAO_DATABASE_STATIC_ROLE_NAME"),
            RequiredPathSegment(
                "OPENBAO_DATABASE_MIGRATOR_STATIC_ROLE_NAME"),
            Required(
                "OPENBAO_DATABASE_ROTATION_PERIOD"));
    }

    public string ReadBootstrapPassword()
        => ReadPasswordFile(
            BootstrapPasswordFile
                ?? throw new InvalidOperationException(
                    "DATABASE_BOOTSTRAP_PASSWORD_FILE is required only during first bootstrap."),
            "bootstrap");

    public string CreateInitialMigratorPassword()
        => MigratorPasswordFile is null
            ? CreateRandomPassword()
            : ReadPasswordFile(
                MigratorPasswordFile,
                "migration");

    public bool RetireBootstrapCredential =>
        string.Equals(
            Environment.GetEnvironmentVariable(
                "RETIRE_DATABASE_BOOTSTRAP_CREDENTIAL"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string? OptionalPath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Path.GetFullPath(value.Trim());
    }

    private static string ReadPasswordFile(
        string passwordFile,
        string purpose)
    {
        try
        {
            // Npgsql requires an immutable string; decode once and clear the
            // source file bytes while explicitly accepting that boundary limitation.
            return SecretTextFileReader.Read(
                passwordFile,
                $"The PostgreSQL {purpose} password file is empty.");
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The PostgreSQL {purpose} password file could not be read.",
                exception);
        }
    }

    private static string CreateRandomPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        try
        {
            // Npgsql requires an immutable password string that cannot be cleared;
            // the random source bytes are still cleared immediately after encoding.
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string RequiredRole(string name)
    {
        var value = Required(name);

        return value.Length <= 63
            ? value
            : throw new InvalidOperationException(
                $"{name} cannot exceed 63 characters.");
    }

    private static string RequiredPathSegment(
        string name)
    {
        var value = Required(name);

        if (value.Contains('/')
            || value.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name} must be a single path segment.");
        }

        return value;
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(
            name);

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"{name} is not configured.")
            : value.Trim();
    }
}
