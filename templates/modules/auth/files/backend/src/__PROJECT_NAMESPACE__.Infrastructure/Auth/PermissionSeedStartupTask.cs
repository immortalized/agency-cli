using __PROJECT_NAMESPACE__.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class PermissionSeedStartupTask(
    IServiceScopeFactory scopeFactory)
    : IDatabaseStartupTask
{
    public async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scope =
            scopeFactory.CreateAsyncScope();

        var seeder = scope.ServiceProvider
            .GetRequiredService<PermissionSeeder>();

        await seeder.SeedAsync(cancellationToken);
    }
}
