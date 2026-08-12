using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AdminCreateUserRequest(
    [param: Required]
    [param: MaxLength(64)]
    string Username,

    [param: EmailAddress]
    [param: MaxLength(320)]
    string? Email,

    // Optional. Omit to receive only the built-in default role. Supplying any
    // role additionally requires the 'users.assign-roles' permission.
    IReadOnlyList<string>? Roles);
