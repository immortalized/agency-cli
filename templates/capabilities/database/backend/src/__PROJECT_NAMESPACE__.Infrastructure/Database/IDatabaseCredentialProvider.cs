namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public interface IDatabaseCredentialProvider
{
    Task<DatabaseCredential> GetCredentialAsync(
        CancellationToken cancellationToken = default);
}
