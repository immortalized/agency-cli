using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record LoginRequest(
    [property: Required]
    [property: MaxLength(320)]
    string Identifier,

    [property: Required]
    [property: MaxLength(1024)]
    string Password);