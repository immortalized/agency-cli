using Microsoft.Extensions.Configuration;
using Npgsql;

namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public static class
    DatabaseConnectionStringFactory
{
    public static string Create(
        IConfiguration configuration,
        string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(
            configuration);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            baseDirectory);

        var options = configuration
            .GetRequiredSection(
                DatabaseOptions.SectionName)
            .Get<DatabaseOptions>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{DatabaseOptions.SectionName}' is missing.");

        ValidateOptions(options);

        var passwordFile =
            ResolvePasswordFile(
                options.PasswordFile,
                baseDirectory);

        var password =
            ReadPassword(passwordFile);

        var builder =
            new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Database = options.Name,
                Username = options.Username,
                Password = password,
                Pooling = true,
                IncludeErrorDetail = false
            };

        return builder.ConnectionString;
    }

    private static void ValidateOptions(
        DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(
                options.Host))
        {
            throw new InvalidOperationException(
                "Database:Host must be configured.");
        }

        if (options.Port is < 1 or > 65_535)
        {
            throw new InvalidOperationException(
                "Database:Port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(
                options.Name))
        {
            throw new InvalidOperationException(
                "Database:Name must be configured.");
        }

        if (string.IsNullOrWhiteSpace(
                options.Username))
        {
            throw new InvalidOperationException(
                "Database:Username must be configured.");
        }

        if (string.IsNullOrWhiteSpace(
                options.PasswordFile))
        {
            throw new InvalidOperationException(
                "Database:PasswordFile must be configured.");
        }
    }

    private static string ResolvePasswordFile(
        string configuredPath,
        string baseDirectory)
    {
        if (Path.IsPathFullyQualified(
                configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(
            Path.Combine(
                baseDirectory,
                configuredPath));
    }

    private static string ReadPassword(
        string passwordFile)
    {
        if (!File.Exists(passwordFile))
        {
            throw new FileNotFoundException(
                "Database password secret was not found.",
                passwordFile);
        }

        string password;

        try
        {
            password =
                File.ReadAllText(
                    passwordFile)
                    .Trim();
        }
        catch (Exception exception)
            when (
                exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Database password secret could not be read.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(
                password))
        {
            throw new InvalidOperationException(
                "Database password secret is empty.");
        }

        if (password.Length < 32)
        {
            throw new InvalidOperationException(
                "Database password secret must contain at least 32 characters.");
        }

        return password;
    }
}