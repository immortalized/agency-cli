namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class DatabaseCredentialOptions
{
    public const string SectionName =
        "DatabaseCredential";

    public string Username { get; init; }
        = string.Empty;

    public string PasswordFile { get; init; }
        = string.Empty;
}
