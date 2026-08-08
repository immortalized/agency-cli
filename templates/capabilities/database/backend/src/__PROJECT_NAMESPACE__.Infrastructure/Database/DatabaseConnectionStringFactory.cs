using Microsoft.Extensions.Configuration;
using Npgsql;

namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public static class DatabaseConnectionStringFactory
{
    public static string CreateForMigration(
        IConfiguration configuration,
        string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var database = GetDatabaseOptions(
            configuration);

        var migration = configuration
            .GetRequiredSection(
                DatabaseMigrationOptions.SectionName)
            .Get<DatabaseMigrationOptions>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{DatabaseMigrationOptions.SectionName}' is missing.");

        ValidateCredential(
            migration.Username,
            migration.PasswordFile,
            DatabaseMigrationOptions.SectionName);

        var passwordFile = ResolvePasswordFile(
            migration.PasswordFile,
            projectRoot);

        return Create(
            database,
            new DatabaseCredential(
                migration.Username,
                ReadPassword(passwordFile)));
    }

    public static string Create(
        DatabaseOptions options,
        DatabaseCredential credential)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);

        ValidateDatabase(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            credential.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            credential.Password);

        return new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.Name,
            Username = credential.Username,
            Password = credential.Password,
            Pooling = true,
            ConnectionLifetime = 300,
            ConnectionIdleLifetime = 60,
            GssEncryptionMode =
                GssEncryptionMode.Disable,
            IncludeErrorDetail = false
        }.ConnectionString;
    }

    public static DatabaseOptions GetDatabaseOptions(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetRequiredSection(
                DatabaseOptions.SectionName)
            .Get<DatabaseOptions>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{DatabaseOptions.SectionName}' is missing.");

        ValidateDatabase(options);
        return options;
    }

    public static DatabaseCredentialOptions
        GetRuntimeCredentialOptions(
            IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetRequiredSection(
                DatabaseCredentialOptions.SectionName)
            .Get<DatabaseCredentialOptions>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{DatabaseCredentialOptions.SectionName}' is missing.");

        ValidateCredential(
            options.Username,
            options.PasswordFile,
            DatabaseCredentialOptions.SectionName);

        return options;
    }

    public static string ResolvePasswordFile(
        string configuredPath,
        string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configuredPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            baseDirectory);

        return Path.IsPathFullyQualified(configuredPath)
            ? configuredPath
            : Path.GetFullPath(
                Path.Combine(
                    baseDirectory,
                    configuredPath));
    }

    private static void ValidateDatabase(
        DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException(
                "Database:Host must be configured.");
        }

        if (options.Port is < 1 or > 65_535)
        {
            throw new InvalidOperationException(
                "Database:Port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new InvalidOperationException(
                "Database:Name must be configured.");
        }
    }

    private static void ValidateCredential(
        string username,
        string passwordFile,
        string sectionName)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                $"{sectionName}:Username must be configured.");
        }

        if (string.IsNullOrWhiteSpace(passwordFile))
        {
            throw new InvalidOperationException(
                $"{sectionName}:PasswordFile must be configured.");
        }
    }

    private static string ReadPassword(
        string passwordFile)
    {
        try
        {
            var password = File.ReadAllText(
                    passwordFile)
                .Trim();

            if (password.Length < 32)
            {
                throw new InvalidOperationException(
                    "Database password secret must contain at least 32 characters.");
            }

            return password;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Database password secret could not be read.",
                exception);
        }
    }
}
