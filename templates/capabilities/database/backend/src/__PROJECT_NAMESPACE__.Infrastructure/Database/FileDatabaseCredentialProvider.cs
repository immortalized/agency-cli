namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class FileDatabaseCredentialProvider(
    DatabaseCredentialOptions options,
    string configurationBaseDirectory)
    : IDatabaseCredentialProvider
{
    public async Task<DatabaseCredential>
        GetCredentialAsync(
            CancellationToken cancellationToken = default)
    {
        var passwordFile =
            DatabaseConnectionStringFactory
                .ResolvePasswordFile(
                    options.PasswordFile,
                    configurationBaseDirectory);

        string password;

        try
        {
            password = (await File.ReadAllTextAsync(
                    passwordFile,
                    cancellationToken))
                .Trim();
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The database credential file could not be read.",
                exception);
        }

        if (password.Length < 32)
        {
            throw new InvalidOperationException(
                "The database credential file must contain at least 32 characters.");
        }

        return new DatabaseCredential(
            options.Username,
            password);
    }
}
