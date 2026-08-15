using System.ComponentModel.DataAnnotations;
using __PROJECT_NAMESPACE__.Application.Auth;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record LoginRequest(
    [param: Required]
    [param: MaxLength(320)]
    string Identifier,

    [param: Required]
    [param: MinLength(PasswordPolicy.MinimumLength)]
    [param: MaxLength(1024)]
    string Password);
