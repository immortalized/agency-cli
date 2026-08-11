using __PROJECT_NAMESPACE__.Infrastructure.Database;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace __PROJECT_NAMESPACE__.Operations;

public static class OperationsDatabase
{
    public static async Task<AppDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration =
            new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

        DatabaseCredential credential;
        if (string.Equals(
                Environment.GetEnvironmentVariable("OPERATIONS_ENVIRONMENT"),
                "Production",
                StringComparison.OrdinalIgnoreCase))
        {
            var openBao = OpenBaoToolOptions.FromEnvironment(string.Empty);
            var value = await OpenBaoStaticCredentialReader.ReadAsync(
                openBao,
                openBao.MigratorTokenFile,
                openBao.DatabaseMigratorStaticRoleName,
                cancellationToken);
            credential = new DatabaseCredential(value.Username, value.Password);
        }
        else
        {
            var username = configuration["DatabaseMigration:Username"]
                ?? throw new InvalidOperationException("DatabaseMigration:Username is not configured.");
            var passwordFile = configuration["DatabaseMigration:PasswordFile"]
                ?? throw new InvalidOperationException("DatabaseMigration:PasswordFile is not configured.");
            // Npgsql requires an immutable string; decode once and clear the
            // source file bytes while explicitly accepting that boundary limitation.
            var password = await SecretTextFileReader.ReadAsync(
                Path.GetFullPath(passwordFile),
                "The database migration password file is empty.",
                cancellationToken);
            credential = new DatabaseCredential(username, password);
        }

        var connectionString =
            DatabaseConnectionStringFactory.Create(
                DatabaseConnectionStringFactory
                    .GetDatabaseOptions(
                        configuration),
                credential);

        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new AppDbContext(options);
    }
}
