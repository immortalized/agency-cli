namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AuthUserResponse(
    Guid Id,
    string Username,
    string? Email);