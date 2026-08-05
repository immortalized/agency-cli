namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    AuthUserResponse User);