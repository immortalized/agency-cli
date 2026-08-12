namespace __PROJECT_NAMESPACE__.Api.Contracts.Roles;

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string? Description,
    bool IsSystem,
    bool IsActive,
    // True for the built-in administrator role, whose permission set is
    // re-granted from the installed module catalog on every startup and can
    // therefore not be edited through this API.
    bool IsPermissionSetManaged,
    IReadOnlyList<string> Permissions,
    int UserCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
