using System.ComponentModel.DataAnnotations;
using __PROJECT_NAMESPACE__.Application.Auth;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record RegisterRequest(
    [param: Required]
    [param: MinLength(1)]
    [param: MaxLength(64)]
    string Username,

    [param: EmailAddress]
    [param: MaxLength(320)]
    string? Email,

    [param: Required]
    [param: MinLength(PasswordPolicy.MinimumLength)]
    [param: MaxLength(1024)]
    string Password);
