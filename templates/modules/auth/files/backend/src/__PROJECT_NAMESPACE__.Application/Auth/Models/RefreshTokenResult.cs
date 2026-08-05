namespace __PROJECT_NAMESPACE__.Application.Auth.Models;

public sealed record RefreshTokenResult(
    string PlainTextToken,
    byte[] TokenHash);