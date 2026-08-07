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
        var environment =
            Environment.GetEnvironmentVariable(
                "ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var currentDirectory =
            Directory.GetCurrentDirectory();

        var apiDirectory =
            ResolveApiDirectory(
                currentDirectory);

        var configuration =
            new ConfigurationBuilder()
                .SetBasePath(apiDirectory)
                .AddJsonFile(
                    "appsettings.json",
                    optional: false)
                .AddJsonFile(
                    $"appsettings.{environment}.json",
                    optional: true)
                .AddEnvironmentVariables()
                .Build();

        var connectionString =
            DatabaseConnectionStringFactory.Create(
                configuration,
                apiDirectory);

        var optionsBuilder =
            new DbContextOptionsBuilder<
                AppDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString);

        return new AppDbContext(
            optionsBuilder.Options);
    }

    private static string ResolveApiDirectory(
        string currentDirectory)
    {
        var namespaceName =
            typeof(AppDbContext)
                .Assembly
                .GetName()
                .Name?
                .Replace(
                    ".Infrastructure",
                    string.Empty,
                    StringComparison.Ordinal)
            ?? throw new InvalidOperationException(
                "Unable to determine the project namespace.");

        var candidates = new[]
        {
            Path.Combine(
                currentDirectory,
                "backend",
                "src",
                $"{namespaceName}.Api"),

            Path.Combine(
                currentDirectory,
                "..",
                $"{namespaceName}.Api"),

            Path.Combine(
                currentDirectory,
                "src",
                $"{namespaceName}.Api")
        };

        foreach (var candidate in candidates)
        {
            var fullPath =
                Path.GetFullPath(candidate);

            if (File.Exists(
                    Path.Combine(
                        fullPath,
                        "appsettings.json")))
            {
                return fullPath;
            }
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate the {namespaceName}.Api directory from '{currentDirectory}'.");
    }
}