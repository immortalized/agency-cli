using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AdminCreateUserRequest(
    [param: Required]
    [param: MaxLength(64)]
    string Username,

    [param: EmailAddress]
    [param: MaxLength(320)]
    string? Email,

    [param: MaxLength(64)]
    string? Role);
