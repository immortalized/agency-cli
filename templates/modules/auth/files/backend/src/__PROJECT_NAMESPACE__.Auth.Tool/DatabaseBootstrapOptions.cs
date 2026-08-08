namespace __PROJECT_NAMESPACE__.Auth.Tool;

public sealed record DatabaseBootstrapOptions(
    string Host,
    int Port,
    string DatabaseName,
    string BootstrapUsername,
    string BootstrapPasswordFile,
    string MigratorUsername,
    string MigratorPasswordFile,
    string OpenBaoManagerUsername,
    string RuntimeUsername,
    string LegacyUsername,
    string SecretsMount,
    string ConnectionName,
    string StaticRoleName,
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
            Path.GetFullPath(
                Required(
                    "DATABASE_BOOTSTRAP_PASSWORD_FILE")),
            RequiredRole("DATABASE_MIGRATOR_USERNAME"),
            Path.GetFullPath(
                Required(
                    "DATABASE_MIGRATOR_PASSWORD_FILE")),
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
            Required(
                "OPENBAO_DATABASE_ROTATION_PERIOD"));
    }

    public string ReadBootstrapPassword()
        => ReadPasswordFile(
            BootstrapPasswordFile,
            "bootstrap");

    public string ReadMigratorPassword()
        => ReadPasswordFile(
            MigratorPasswordFile,
            "migration");

    private static string ReadPasswordFile(
        string passwordFile,
        string purpose)
    {
        try
        {
            var password = File.ReadAllText(
                    passwordFile)
                .Trim();

            return string.IsNullOrWhiteSpace(password)
                ? throw new InvalidOperationException(
                    $"The PostgreSQL {purpose} password file is empty.")
                : password;
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
