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
            options.KeyRingFile,
            "Auth:Jwt:KeyRingFile",
            errors);

        if (errors.Count == 0)
        {
            ValidateKeyMaterial(
                options,
                errors);
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

    private static void ValidateKeyMaterial(
        JwtOptions options,
        ICollection<string> errors)
    {
        try
        {
            using var keyRing =
                JwtKeyRing.Load(
                    options.KeyRingFile);

            using var privateKey =
                RsaKeyLoader
                    .LoadPrivateKeyFromFile(
                        options.PrivateKeyFile);

            var activePublicKey =
                keyRing.ValidationKeys
                    .OfType<
                        Microsoft.IdentityModel.Tokens
                            .RsaSecurityKey>()
                    .Single(key =>
                        key.KeyId ==
                        keyRing.ActiveKeyId);

            var privatePublicParameters =
                privateKey.ExportParameters(
                    includePrivateParameters:
                        false);

            var activePublicParameters =
                activePublicKey.Rsa!
                    .ExportParameters(
                        includePrivateParameters:
                            false);

            if (
                privatePublicParameters.Modulus
                    is null
                || activePublicParameters.Modulus
                    is null
                || privatePublicParameters.Exponent
                    is null
                || activePublicParameters.Exponent
                    is null
                || !privatePublicParameters.Modulus
                    .AsSpan()
                    .SequenceEqual(
                        activePublicParameters.Modulus)
                || !privatePublicParameters.Exponent
                    .AsSpan()
                    .SequenceEqual(
                        activePublicParameters.Exponent))
            {
                errors.Add(
                    "JWT private key does not match the active public key.");
            }
        }
        catch (Exception exception)
        {
            errors.Add(
                $"JWT key material is invalid: {exception.Message}");
        }
    }
}