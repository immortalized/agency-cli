using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record BootstrapRequest(
    [property: Required]
    [property: MinLength(3)]
    [property: MaxLength(64)]
    string Username,

    [property: Required]
    [property: MinLength(15)]
    [property: MaxLength(1024)]
    string Password,

    [property: EmailAddress]
    [property: MaxLength(320)]
    string? Email);