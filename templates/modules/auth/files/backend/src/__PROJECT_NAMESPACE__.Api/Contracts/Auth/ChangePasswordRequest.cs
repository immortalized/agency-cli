using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record ChangePasswordRequest(
    [param: Required]
    [param: MaxLength(1024)]
    string CurrentPassword,

    [param: Required]
    [param: MaxLength(1024)]
    string NewPassword);
