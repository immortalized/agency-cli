using Microsoft.Extensions.Hosting;

namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class DatabaseCredentialInitializer(
    IDatabaseCredentialProvider credentialProvider,
    DatabaseCredentialState credentialState,
    IEnumerable<IDatabaseStartupTask> startupTasks)
    : IHostedService
{
    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        var credential =
            await credentialProvider
                .GetCredentialAsync(
                    cancellationToken);

        credentialState.Initialize(credential);

        foreach (var startupTask in startupTasks)
        {
            await startupTask.ExecuteAsync(
                cancellationToken);
        }
    }

    public Task StopAsync(
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
