using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record LoginRequest(
    [param: Required]
    [param: MaxLength(320)]
    string Identifier,

    [param: Required]
    [param: MaxLength(1024)]
    string Password);
