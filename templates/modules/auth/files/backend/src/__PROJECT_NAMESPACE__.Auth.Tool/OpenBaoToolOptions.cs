namespace __PROJECT_NAMESPACE__.Auth.Tool;

public sealed record OpenBaoToolOptions(
    Uri Address,
    string TransitMount,
    string KeyName,
    string RuntimePolicyName,
    string RuntimeTokenFile,
    string BootstrapToken,
    TimeSpan RequestTimeout)
{
    public static OpenBaoToolOptions FromEnvironment()
    {
        var addressValue = Required(
            "OPENBAO_ADDRESS");

        if (!Uri.TryCreate(
                addressValue,
                UriKind.Absolute,
                out var address)
            || (address.Scheme != Uri.UriSchemeHttp
                && address.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "OPENBAO_ADDRESS must be an absolute HTTP or HTTPS address.");
        }

        var transitMount = RequiredPathSegment(
            "OPENBAO_TRANSIT_MOUNT");

        var keyName = RequiredPathSegment(
            "OPENBAO_JWT_KEY_NAME");

        var policyName = RequiredPathSegment(
            "OPENBAO_RUNTIME_POLICY_NAME");

        var timeoutValue = Required(
            "OPENBAO_REQUEST_TIMEOUT_SECONDS");

        if (!int.TryParse(
                timeoutValue,
                out var timeoutSeconds)
            || timeoutSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException(
                "OPENBAO_REQUEST_TIMEOUT_SECONDS must be between 1 and 60.");
        }

        return new OpenBaoToolOptions(
            address,
            transitMount,
            keyName,
            policyName,
            Path.GetFullPath(
                Required(
                    "OPENBAO_RUNTIME_TOKEN_FILE")),
            Required(
                "OPENBAO_BOOTSTRAP_TOKEN"),
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static string RequiredPathSegment(
        string name)
    {
        var value = Required(name);

        if (value.Contains('/')
            || value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name} must be a single path segment.");
        }

        return value;
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(
            name);

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"{name} is not configured.")
            : value.Trim();
    }
}
