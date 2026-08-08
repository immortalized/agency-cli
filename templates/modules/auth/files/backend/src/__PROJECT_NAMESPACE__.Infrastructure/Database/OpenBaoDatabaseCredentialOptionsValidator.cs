using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class
    OpenBaoDatabaseCredentialOptionsValidator
    : IValidateOptions<
        OpenBaoDatabaseCredentialOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        OpenBaoDatabaseCredentialOptions options)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(
                options.Address,
                UriKind.Absolute,
                out var address)
            || (address.Scheme != Uri.UriSchemeHttp
                && address.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(
                "OpenBaoDatabaseCredential:Address must be an absolute HTTP or HTTPS address.");
        }

        ValidatePathSegment(
            options.SecretsMount,
            "OpenBaoDatabaseCredential:SecretsMount",
            errors);

        ValidatePathSegment(
            options.StaticRoleName,
            "OpenBaoDatabaseCredential:StaticRoleName",
            errors);

        if (string.IsNullOrWhiteSpace(
                options.TokenFile)
            || !Path.IsPathFullyQualified(
                options.TokenFile))
        {
            errors.Add(
                "OpenBaoDatabaseCredential:TokenFile must be an absolute path.");
        }
        else if (!File.Exists(options.TokenFile))
        {
            errors.Add(
                $"OpenBaoDatabaseCredential:TokenFile does not exist: '{options.TokenFile}'.");
        }

        if (options.RequestTimeoutSeconds
            is < 1 or > 60)
        {
            errors.Add(
                "OpenBaoDatabaseCredential:RequestTimeoutSeconds must be between 1 and 60.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidatePathSegment(
        string value,
        string configurationName,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('/')
            || value.Contains(
                "..",
                StringComparison.Ordinal))
        {
            errors.Add(
                $"{configurationName} must be a single non-empty path segment.");
        }
    }
}
