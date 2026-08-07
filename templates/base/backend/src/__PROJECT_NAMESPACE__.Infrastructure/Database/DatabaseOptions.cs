namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class DatabaseOptions
{
    public const string SectionName =
        "Database";

    public string Host { get; init; }
        = string.Empty;

    public int Port { get; init; }
        = 5432;

    public string Name { get; init; }
        = string.Empty;

    public string Username { get; init; }
        = string.Empty;

    public string PasswordFile { get; init; }
        = string.Empty;
}