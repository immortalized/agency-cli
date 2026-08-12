using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Roles;

public sealed record UpdateRoleRequest(
    [param: Required]
    [param: MaxLength(64)]
    string Name,

    [param: MaxLength(128)]
    string? DisplayName,

    [param: MaxLength(256)]
    string? Description,

    // Replaces the role's permission set outright. Send the full desired set,
    // not a delta. Omit only for the built-in administrator role, whose
    // permission set is managed by module seeding.
    IReadOnlyList<string>? Permissions);
