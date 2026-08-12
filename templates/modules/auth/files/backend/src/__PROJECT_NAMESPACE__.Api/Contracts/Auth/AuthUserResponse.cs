namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AuthUserResponse(
    Guid Id,
    string Username,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);
