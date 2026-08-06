namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int RefreshTokenLifetimeDays { get; init; } = 14;

    public string RefreshCookieName { get; init; }
        = "__Host-refresh-token";
}