using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Roles;

public sealed record CreateRoleRequest(
    [param: Required]
    [param: MaxLength(64)]
    string Name,

    [param: MaxLength(128)]
    string? DisplayName,

    [param: MaxLength(256)]
    string? Description,

    IReadOnlyList<string>? Permissions);
