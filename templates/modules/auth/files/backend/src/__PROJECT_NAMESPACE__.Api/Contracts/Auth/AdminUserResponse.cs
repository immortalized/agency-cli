namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AdminUserResponse(
    Guid Id,
    string Username,
    string? Email,
    string Role,
    IReadOnlyList<string> Permissions,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset? PasswordChangedAtUtc,
    DateTimeOffset? DisabledAtUtc);
