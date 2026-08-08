using __PROJECT_NAMESPACE__.Infrastructure.Database;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace __PROJECT_NAMESPACE__.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection
        AddDatabaseInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration,
            string configurationBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configurationBaseDirectory);

        var databaseOptions =
            DatabaseConnectionStringFactory
                .GetDatabaseOptions(configuration);

        services.AddSingleton(databaseOptions);

        services.TryAddSingleton<
            IDatabaseCredentialProvider>(
            _ => new FileDatabaseCredentialProvider(
                DatabaseConnectionStringFactory
                    .GetRuntimeCredentialOptions(
                        configuration),
                configurationBaseDirectory));

        services.AddSingleton<DatabaseCredentialState>();
        services.AddHostedService<
            DatabaseCredentialInitializer>();

        services.AddDbContext<AppDbContext>(
            (serviceProvider, options) =>
            {
                var credential = serviceProvider
                    .GetRequiredService<
                        DatabaseCredentialState>()
                    .Credential;

                options.UseNpgsql(
                    DatabaseConnectionStringFactory
                        .Create(
                            databaseOptions,
                            credential));
            });

        return services;
    }
}
