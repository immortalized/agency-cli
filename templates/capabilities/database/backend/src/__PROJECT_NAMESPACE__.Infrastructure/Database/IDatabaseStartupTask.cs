namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public interface IDatabaseStartupTask
{
    Task ExecuteAsync(
        CancellationToken cancellationToken = default);
}
