namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class DatabaseMigrationOptions
{
    public const string SectionName =
        "DatabaseMigration";

    public string Username { get; init; }
        = string.Empty;

    public string PasswordFile { get; init; }
        = string.Empty;
}
