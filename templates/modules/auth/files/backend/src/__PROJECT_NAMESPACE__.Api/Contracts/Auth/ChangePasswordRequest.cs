using System.ComponentModel.DataAnnotations;
using __PROJECT_NAMESPACE__.Application.Auth;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record ChangePasswordRequest(
    [param: Required]
    [param: MinLength(PasswordPolicy.MinimumLength)]
    [param: MaxLength(1024)]
    string CurrentPassword,

    [param: Required]
    [param: MinLength(PasswordPolicy.MinimumLength)]
    [param: MaxLength(1024)]
    string NewPassword);
