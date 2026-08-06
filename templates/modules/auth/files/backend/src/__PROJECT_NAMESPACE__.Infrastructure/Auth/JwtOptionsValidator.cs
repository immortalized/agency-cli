using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class JwtOptionsValidator
    : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(
                options.Issuer))
        {
            errors.Add(
                "Auth:Jwt:Issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(
                options.Audience))
        {
            errors.Add(
                "Auth:Jwt:Audience must be configured.");
        }

        if (
            options.AccessTokenLifetimeMinutes
            is < 1 or > 30)
        {
            errors.Add(
                "Auth:Jwt:AccessTokenLifetimeMinutes must be between 1 and 30.");
        }

        ValidateFile(
            options.PrivateKeyFile,
            "Auth:Jwt:PrivateKeyFile",
            errors);

        ValidateFile(
            options.PublicKeyFile,
            "Auth:Jwt:PublicKeyFile",
            errors);

        if (string.IsNullOrWhiteSpace(
                options.KeyId))
        {
            errors.Add(
                "Auth:Jwt:KeyId must be configured.");
        }
        else if (options.KeyId.Length > 128)
        {
            errors.Add(
                "Auth:Jwt:KeyId cannot exceed 128 characters.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateFile(
        string filePath,
        string configurationName,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            errors.Add(
                $"{configurationName} must be configured.");

            return;
        }

        if (!Path.IsPathFullyQualified(filePath))
        {
            errors.Add(
                $"{configurationName} must be an absolute path.");

            return;
        }

        if (!File.Exists(filePath))
        {
            errors.Add(
                $"{configurationName} does not exist: '{filePath}'.");
        }
    }
}