namespace __PROJECT_NAMESPACE__.Application.Auth.Models;

public sealed record AccessTokenResult(
    string Token,
    DateTimeOffset ExpiresAtUtc);