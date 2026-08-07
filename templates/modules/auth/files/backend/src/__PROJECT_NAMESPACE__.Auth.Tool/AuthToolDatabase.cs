using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class AuthToolDatabase
{
    public static AppDbContext CreateDbContext()
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__Database");

        if (string.IsNullOrWhiteSpace(
                connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__Database is not configured.");
        }

        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new AppDbContext(options);
    }
}