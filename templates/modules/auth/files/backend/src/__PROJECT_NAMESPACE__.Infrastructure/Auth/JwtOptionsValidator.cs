using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class JwtOptionsValidator
    : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        JwtOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            errors.Add(
                "Auth:Jwt:Issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            errors.Add(
                "Auth:Jwt:Audience must be configured.");
        }

        if (options.AccessTokenLifetimeMinutes is < 1 or > 30)
        {
            errors.Add(
                "Auth:Jwt:AccessTokenLifetimeMinutes must be between 1 and 30.");
        }

        if (string.IsNullOrWhiteSpace(options.PrivateKeyPem))
        {
            errors.Add(
                "Auth:Jwt:PrivateKeyPem must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.PublicKeyPem))
        {
            errors.Add(
                "Auth:Jwt:PublicKeyPem must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.KeyId))
        {
            errors.Add(
                "Auth:Jwt:KeyId must be configured.");
        }

        if (options.KeyId.Length > 128)
        {
            errors.Add(
                "Auth:Jwt:KeyId cannot exceed 128 characters.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}