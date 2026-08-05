namespace __PROJECT_NAMESPACE__.Application.Auth.Models;

public sealed record AccessTokenSubject(
    Guid UserId,
    string Username,
    string? Email);