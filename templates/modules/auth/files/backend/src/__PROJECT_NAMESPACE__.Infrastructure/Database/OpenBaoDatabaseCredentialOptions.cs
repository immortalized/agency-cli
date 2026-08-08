namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class OpenBaoDatabaseCredentialOptions
{
    public const string SectionName =
        "OpenBaoDatabaseCredential";

    public string Address { get; init; }
        = string.Empty;

    public string SecretsMount { get; init; }
        = "database";

    public string StaticRoleName { get; init; }
        = string.Empty;

    public string TokenFile { get; init; }
        = string.Empty;

    public int RequestTimeoutSeconds { get; init; }
        = 10;
}
