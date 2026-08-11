namespace __PROJECT_NAMESPACE__.Operations;

public sealed record OpenBaoToolOptions(
    Uri Address,
    string TransitMount,
    string KeyName,
    string DatabaseMount,
    string DatabaseConnectionName,
    string DatabaseStaticRoleName,
    string DatabaseMigratorStaticRoleName,
    string RuntimePolicyName,
    string RuntimeTokenFile,
    string MigratorPolicyName,
    string MigratorTokenFile,
    string JwtRotationPolicyName,
    string JwtRotationTokenFile,
    string DatabaseRotationPolicyName,
    string DatabaseRotationTokenFile,
    string ProvisioningTokenFile,
    string BootstrapToken,
    TimeSpan RequestTimeout)
{
    public static OpenBaoToolOptions FromEnvironment(
        string? bootstrapToken = null)
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

        var databaseMigratorStaticRoleName =
            RequiredPathSegment(
                "OPENBAO_DATABASE_MIGRATOR_STATIC_ROLE_NAME");

        var policyName = RequiredPathSegment(
            "OPENBAO_RUNTIME_POLICY_NAME");

        var migratorPolicyName = RequiredPathSegment(
            "OPENBAO_MIGRATOR_POLICY_NAME");

        var jwtRotationPolicyName = RequiredPathSegment(
            "OPENBAO_JWT_ROTATION_POLICY_NAME");

        var databaseRotationPolicyName = RequiredPathSegment(
            "OPENBAO_DATABASE_ROTATION_POLICY_NAME");

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
            databaseMigratorStaticRoleName,
            policyName,
            Path.GetFullPath(
                Required(
                    "OPENBAO_RUNTIME_TOKEN_FILE")),
            migratorPolicyName,
            Path.GetFullPath(
                Required("OPENBAO_MIGRATOR_TOKEN_FILE")),
            jwtRotationPolicyName,
            Path.GetFullPath(
                Required("OPENBAO_JWT_ROTATION_TOKEN_FILE")),
            databaseRotationPolicyName,
            Path.GetFullPath(
                Required("OPENBAO_DATABASE_ROTATION_TOKEN_FILE")),
            Path.GetFullPath(
                Required("OPENBAO_PROVISIONING_TOKEN_FILE")),
            bootstrapToken ?? ReadDevelopmentBootstrapToken(
                Path.GetFullPath(Required("OPENBAO_BOOTSTRAP_MATERIAL_FILE"))),
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

        // The development client ultimately needs this value as an HTTP header.
        // .NET header APIs require an immutable string that cannot be zeroed; keep
        // this development-only token short-lived and never persist or log it.
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
