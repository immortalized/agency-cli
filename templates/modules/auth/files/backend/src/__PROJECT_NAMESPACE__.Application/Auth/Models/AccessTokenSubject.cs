namespace __PROJECT_NAMESPACE__.Application.Auth.Models;

public sealed record AccessTokenSubject(
    Guid UserId,
    string Username,
    string? Email,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    long AuthVersion,
    bool MustChangePassword);
