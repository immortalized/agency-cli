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

        if (options.AccessTokenLifetimeMinutes
            is < 1 or > 30)
        {
            errors.Add(
                "Auth:Jwt:AccessTokenLifetimeMinutes must be between 1 and 30.");
        }

        ValidateAbsoluteFile(
            options.KeyRingFile,
            "Auth:Jwt:KeyRingFile",
            errors);

        ValidateOpenBaoOptions(
            options.OpenBao,
            errors);

        if (errors.Count == 0)
        {
            try
            {
                using var keyRing = JwtKeyRing.Load(
                    options.KeyRingFile);
            }
            catch (Exception exception)
            {
                errors.Add(
                    $"JWT public key ring is invalid: {exception.Message}");
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateOpenBaoOptions(
        OpenBaoJwtSigningOptions options,
        ICollection<string> errors)
    {
        if (!Uri.TryCreate(
                options.Address,
                UriKind.Absolute,
                out var address)
            || (address.Scheme != Uri.UriSchemeHttp
                && address.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(
                "Auth:Jwt:OpenBao:Address must be an absolute HTTP or HTTPS address.");
        }

        ValidatePathSegment(
            options.TransitMount,
            "Auth:Jwt:OpenBao:TransitMount",
            errors);

        ValidatePathSegment(
            options.KeyName,
            "Auth:Jwt:OpenBao:KeyName",
            errors);

        ValidateAbsoluteFile(
            options.TokenFile,
            "Auth:Jwt:OpenBao:TokenFile",
            errors);

        if (options.RequestTimeoutSeconds
            is < 1 or > 60)
        {
            errors.Add(
                "Auth:Jwt:OpenBao:RequestTimeoutSeconds must be between 1 and 60.");
        }
    }

    private static void ValidatePathSegment(
        string value,
        string configurationName,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal))
        {
            errors.Add(
                $"{configurationName} must be a single non-empty path segment.");
        }
    }

    private static void ValidateAbsoluteFile(
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
