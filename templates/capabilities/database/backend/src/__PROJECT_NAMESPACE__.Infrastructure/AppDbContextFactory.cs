using __PROJECT_NAMESPACE__.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace __PROJECT_NAMESPACE__.Infrastructure.Persistence;

public sealed class AppDbContextFactory
    : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(
        string[] args)
    {
        var projectRoot = ResolveProjectRoot(
            Directory.GetCurrentDirectory());

        var namespaceName = typeof(AppDbContext)
            .Assembly
            .GetName()
            .Name?
            .Replace(
                ".Infrastructure",
                string.Empty,
                StringComparison.Ordinal)
            ?? throw new InvalidOperationException(
                "Unable to determine the project namespace.");

        var apiSettings = Path.Combine(
            "backend",
            "src",
            $"{namespaceName}.Api",
            "appsettings.json");

        var configuration =
            new ConfigurationBuilder()
                .SetBasePath(projectRoot)
                .AddJsonFile(
                    apiSettings,
                    optional: false)
                .AddJsonFile(
                    "database.migrations.json",
                    optional: false)
                .AddEnvironmentVariables()
                .Build();

        var connectionString =
            DatabaseConnectionStringFactory
                .CreateForMigration(
                    configuration,
                    projectRoot);

        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new AppDbContext(options);
    }

    private static string ResolveProjectRoot(
        string startDirectory)
    {
        var current = new DirectoryInfo(
            Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        ".agency.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate the Agency project root from '{startDirectory}'.");
    }
}
