namespace __PROJECT_NAMESPACE__.Auth.Tool;

public sealed record OpenBaoToolOptions(
    Uri Address,
    string TransitMount,
    string KeyName,
    string DatabaseMount,
    string DatabaseConnectionName,
    string DatabaseStaticRoleName,
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

        var databaseMount = RequiredPathSegment(
            "OPENBAO_DATABASE_MOUNT");

        var databaseConnectionName =
            RequiredPathSegment(
                "OPENBAO_DATABASE_CONNECTION_NAME");

        var databaseStaticRoleName =
            RequiredPathSegment(
                "OPENBAO_DATABASE_STATIC_ROLE_NAME");

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
            databaseMount,
            databaseConnectionName,
            databaseStaticRoleName,
            policyName,
            Path.GetFullPath(
                Required(
                    "OPENBAO_RUNTIME_TOKEN_FILE")),
            ReadDevelopmentBootstrapToken(
                Path.GetFullPath(
                    Required(
                        "OPENBAO_BOOTSTRAP_MATERIAL_FILE"))),
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static string ReadDevelopmentBootstrapToken(
        string materialFile)
    {
        const string prefix = "Initial Root Token: ";

        if (!File.Exists(materialFile))
        {
            throw new InvalidOperationException(
                "The development OpenBao bootstrap material is missing. Wait for the openbao-bootstrap service to initialize or unseal OpenBao.");
        }

        var token = File.ReadLines(materialFile)
            .Where(line => line.StartsWith(
                prefix,
                StringComparison.Ordinal))
            .Select(line => line[prefix.Length..].Trim())
            .SingleOrDefault();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "The development OpenBao bootstrap material does not contain a root token.");
        }

        return token;
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
