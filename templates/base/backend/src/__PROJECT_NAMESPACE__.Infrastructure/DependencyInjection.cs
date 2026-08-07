using __PROJECT_NAMESPACE__.Infrastructure.Database;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace __PROJECT_NAMESPACE__.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection
        AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration,
            string configurationBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(
            services);

        ArgumentNullException.ThrowIfNull(
            configuration);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            configurationBaseDirectory);

        var connectionString =
            DatabaseConnectionStringFactory.Create(
                configuration,
                configurationBaseDirectory);

        services.AddDbContext<AppDbContext>(
            options =>
                options.UseNpgsql(
                    connectionString));

        return services;
    }
}