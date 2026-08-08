using __PROJECT_NAMESPACE__.Infrastructure.Database;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class AuthToolDatabase
{
    public static AppDbContext CreateDbContext()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

        var username = configuration[
            "DatabaseMigration:Username"]
            ?? throw new InvalidOperationException(
                "DatabaseMigration:Username is not configured.");

        var passwordFile = configuration[
            "DatabaseMigration:PasswordFile"]
            ?? throw new InvalidOperationException(
                "DatabaseMigration:PasswordFile is not configured.");

        var password = File.ReadAllText(
                Path.GetFullPath(passwordFile))
            .Trim();

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "The database migration password file is empty.");
        }

        var connectionString =
            DatabaseConnectionStringFactory.Create(
                DatabaseConnectionStringFactory
                    .GetDatabaseOptions(
                        configuration),
                new DatabaseCredential(
                    username,
                    password));

        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new AppDbContext(options);
    }
}
