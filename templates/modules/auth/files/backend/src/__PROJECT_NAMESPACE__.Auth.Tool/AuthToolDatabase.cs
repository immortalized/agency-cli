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

        var connectionString =
            DatabaseConnectionStringFactory.Create(
                configuration,
                AppContext.BaseDirectory);

        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new AppDbContext(options);
    }
}
