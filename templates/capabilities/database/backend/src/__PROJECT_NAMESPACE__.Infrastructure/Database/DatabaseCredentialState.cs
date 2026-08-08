namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class DatabaseCredentialState
{
    private DatabaseCredential? _credential;

    public DatabaseCredential Credential =>
        Volatile.Read(ref _credential)
        ?? throw new InvalidOperationException(
            "The runtime database credential has not been initialized.");

    public void Initialize(
        DatabaseCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (Interlocked.CompareExchange(
                ref _credential,
                credential,
                null) is not null)
        {
            throw new InvalidOperationException(
                "The runtime database credential has already been initialized.");
        }
    }
}
