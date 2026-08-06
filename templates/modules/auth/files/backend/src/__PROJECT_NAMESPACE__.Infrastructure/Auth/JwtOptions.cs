namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName =
        "Auth:Jwt";

    public string Issuer { get; init; }
        = string.Empty;

    public string Audience { get; init; }
        = string.Empty;

    public int AccessTokenLifetimeMinutes
    {
        get;
        init;
    } = 10;

    public string PrivateKeyFile { get; init; }
        = string.Empty;

    public string KeyRingFile { get; init; }
        = string.Empty;
}