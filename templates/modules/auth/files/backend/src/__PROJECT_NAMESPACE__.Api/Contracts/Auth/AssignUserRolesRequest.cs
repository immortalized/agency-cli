using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AssignUserRolesRequest(
    [param: Required]
    IReadOnlyList<string> Roles);
