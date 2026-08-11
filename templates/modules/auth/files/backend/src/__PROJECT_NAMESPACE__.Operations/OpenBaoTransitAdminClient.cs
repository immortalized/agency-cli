using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace __PROJECT_NAMESPACE__.Operations;

public sealed class OpenBaoTransitAdminClient(
    HttpClient httpClient,
    OpenBaoToolOptions options)
{
    private readonly HttpClient _httpClient = httpClient
        ?? throw new ArgumentNullException(
            nameof(httpClient));

    private readonly OpenBaoToolOptions _options =
        options
        ?? throw new ArgumentNullException(
            nameof(options));

    public async Task EnsureTransitEnabledAsync(
        CancellationToken cancellationToken)
    {
        await OpenBaoAdminRetry.ExecuteAsync(
            EnsureTransitEnabledOnceAsync,
            "enable the Transit secrets engine while Raft leadership settles",
            cancellationToken);
    }

    private async Task EnsureTransitEnabledOnceAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "v1/sys/mounts",
            null,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            "read enabled secrets engines",
            cancellationToken);

        using var document = await ReadJsonAsync(
            response,
            cancellationToken);

        var data = RequiredProperty(
            document.RootElement,
            "data");

        if (data.TryGetProperty(
                $"{_options.TransitMount}/",
                out var mount))
        {
            var type = RequiredString(
                mount,
                "type");

            if (!string.Equals(
                    type,
                    "transit",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"OpenBao mount '{_options.TransitMount}' is not a Transit secrets engine.");
            }

            return;
        }

        using var createResponse = await SendAsync(
            HttpMethod.Post,
            $"v1/sys/mounts/{Escape(_options.TransitMount)}",
            new
            {
                type = "transit",
                description =
                    "Development JWT signing for this generated project"
            },
            cancellationToken);

        await EnsureSuccessAsync(
            createResponse,
            "enable the Transit secrets engine",
            cancellationToken);
    }

    public async Task<OpenBaoTransitKey?>
        ReadKeyAsync(
            CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            KeyPath,
            null,
            cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(
            response,
            "read the Transit signing key",
            cancellationToken);

        using var document = await ReadJsonAsync(
            response,
            cancellationToken);

        return ParseKey(document.RootElement);
    }

    public async Task CreateKeyAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            KeyPath,
            new
            {
                type = "rsa-3072",
                exportable = false,
                allow_plaintext_backup = false
            },
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            "create the Transit RSA-3072 signing key",
            cancellationToken);
    }

    public async Task RotateKeyAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"{KeyPath}/rotate",
            new { },
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            "rotate the Transit signing key",
            cancellationToken);
    }

    public async Task WriteRuntimePolicyAsync(
        CancellationToken cancellationToken)
    {
        var policy =
            $$"""
            path "{{_options.TransitMount}}/sign/{{_options.KeyName}}/sha2-256" {
              capabilities = ["update"]
            }

            path "{{_options.DatabaseMount}}/static-creds/{{_options.DatabaseStaticRoleName}}" {
              capabilities = ["read"]
            }
            """;

        using var response = await SendAsync(
            HttpMethod.Post,
            $"v1/sys/policies/acl/{Escape(_options.RuntimePolicyName)}",
            new { policy },
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            "create the runtime JWT signing policy",
            cancellationToken);
    }

    public async Task<string> CreateRuntimeTokenAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            ProvisionedTokenCreationPath,
            new
            {
                policies = new[]
                {
                    _options.RuntimePolicyName
                },
                no_default_policy = true,
                renewable = false,
                ttl = "8760h",
                display_name = "generated-api-jwt-signer"
            },
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            "create the runtime API token",
            cancellationToken);

        using var document = await ReadJsonAsync(
            response,
            cancellationToken);

        var auth = RequiredProperty(
            document.RootElement,
            "auth");

        return RequiredString(
            auth,
            "client_token");
    }

    public async Task WriteMigratorPolicyAsync(
        CancellationToken cancellationToken)
    {
        var policy =
            $$"""
            path "{{_options.DatabaseMount}}/static-creds/{{_options.DatabaseMigratorStaticRoleName}}" {
              capabilities = ["read"]
            }
            """;

        using var response = await SendAsync(
            HttpMethod.Post,
            $"v1/sys/policies/acl/{Escape(_options.MigratorPolicyName)}",
            new { policy },
            cancellationToken);
        await EnsureSuccessAsync(
            response,
            "create the migration-only policy",
            cancellationToken);
    }

    public async Task WriteProvisioningPolicyAsync(
        CancellationToken cancellationToken)
    {
        var policy =
            $$"""
            path "sys/mounts" {
              capabilities = ["read", "sudo"]
            }
            path "sys/mounts/{{_options.TransitMount}}" {
              capabilities = ["create", "read", "update", "sudo"]
            }
            path "sys/mounts/{{_options.DatabaseMount}}" {
              capabilities = ["create", "read", "update", "sudo"]
            }
            path "{{_options.TransitMount}}/keys/{{_options.KeyName}}" {
              capabilities = ["create", "read", "update"]
            }
            path "{{_options.DatabaseMount}}/*" {
              capabilities = ["create", "read", "update"]
            }
            path "sys/policies/acl/{{_options.RuntimePolicyName}}" {
              capabilities = ["create", "read", "update", "sudo"]
            }
            path "sys/policies/acl/{{_options.MigratorPolicyName}}" {
              capabilities = ["create", "read", "update", "sudo"]
            }
            path "sys/policies/acl/{{_options.JwtRotationPolicyName}}" {
              capabilities = ["create", "read", "update", "sudo"]
            }
            path "sys/policies/acl/{{_options.DatabaseRotationPolicyName}}" {
              capabilities = ["create", "read", "update", "sudo"]
            }
            path "auth/token/create/{{ProvisioningTokenRoleName}}" {
              capabilities = ["create", "update"]
            }
            path "auth/token/revoke-self" {
              capabilities = ["update"]
            }
            """;

        await OpenBaoAdminRetry.ExecuteAsync(
            async retryCancellationToken =>
            {
                using var response = await SendAsync(
                    HttpMethod.Post,
                    $"v1/sys/policies/acl/{Escape(ProvisioningPolicyName)}",
                    new { policy },
                    retryCancellationToken);
                await EnsureSuccessAsync(
                    response,
                    "create the resumable provisioning policy",
                    retryCancellationToken);
            },
            "create the resumable provisioning policy while Raft leadership settles",
            cancellationToken);
    }

    public async Task WriteProvisioningTokenRoleAsync(
        CancellationToken cancellationToken)
    {
        await OpenBaoAdminRetry.ExecuteAsync(
            WriteProvisioningTokenRoleOnceAsync,
            "create the constrained provisioning token role while Raft leadership settles",
            cancellationToken);
    }

    private async Task WriteProvisioningTokenRoleOnceAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"v1/auth/token/roles/{Escape(ProvisioningTokenRoleName)}",
            new
            {
                allowed_policies = new[]
                {
                    _options.RuntimePolicyName,
                    _options.MigratorPolicyName,
                    _options.JwtRotationPolicyName,
                    _options.DatabaseRotationPolicyName
                },
                disallowed_policies = new[]
                {
                    "root",
                    ProvisioningPolicyName
                },
                orphan = true,
                renewable = true,
                token_no_default_policy = true,
                token_explicit_max_ttl = "8760h",
                token_type = "service"
            },
            cancellationToken);
        await EnsureSuccessAsync(
            response,
            "create the constrained provisioning token role",
            cancellationToken);
    }

    public async Task<string> CreateProvisioningTokenAsync(
        CancellationToken cancellationToken)
        => await OpenBaoAdminRetry.ExecuteAsync(
            CreateProvisioningTokenOnceAsync,
            "create the resumable provisioning token while Raft leadership settles",
            cancellationToken);

    private async Task<string> CreateProvisioningTokenOnceAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "v1/auth/token/create",
            new
            {
                policies = new[] { ProvisioningPolicyName },
                no_default_policy = true,
                no_parent = true,
                renewable = false,
                ttl = "24h",
                explicit_max_ttl = "24h",
                display_name = "generated-resumable-auth-provisioning"
            },
            cancellationToken);
        await EnsureSuccessAsync(
            response,
            "create the resumable provisioning token",
            cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        return RequiredString(
            RequiredProperty(document.RootElement, "auth"),
            "client_token");
    }

    public async Task<string> CreateMigratorTokenAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            ProvisionedTokenCreationPath,
            new
            {
                policies = new[] { _options.MigratorPolicyName },
                no_default_policy = true,
                renewable = true,
                ttl = "720h",
                explicit_max_ttl = "2160h",
                display_name = "generated-operations-database-migrator"
            },
            cancellationToken);
        await EnsureSuccessAsync(response, "create the migration-only token", cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        return RequiredString(
            RequiredProperty(document.RootElement, "auth"),
            "client_token");
    }

    public async Task<string> CreateJwtRotationTokenAsync(
        CancellationToken cancellationToken)
    {
        var policy =
            $$"""
            path "{{_options.TransitMount}}/keys/{{_options.KeyName}}" {
              capabilities = ["read"]
            }
            path "{{_options.TransitMount}}/keys/{{_options.KeyName}}/rotate" {
              capabilities = ["update"]
            }
            """;
        return await WritePolicyAndCreateTokenAsync(
            _options.JwtRotationPolicyName,
            "generated-operations-jwt-rotation",
            policy,
            cancellationToken);
    }

    public async Task<string> CreateDatabaseRotationTokenAsync(
        CancellationToken cancellationToken)
    {
        var policy =
            $$"""
            path "{{_options.DatabaseMount}}/static-creds/{{_options.DatabaseStaticRoleName}}" {
              capabilities = ["read"]
            }
            path "{{_options.DatabaseMount}}/rotate-role/{{_options.DatabaseStaticRoleName}}" {
              capabilities = ["update"]
            }
            """;
        return await WritePolicyAndCreateTokenAsync(
            _options.DatabaseRotationPolicyName,
            "generated-operations-database-rotation",
            policy,
            cancellationToken);
    }

    private async Task<string> WritePolicyAndCreateTokenAsync(
        string policyName,
        string displayName,
        string policy,
        CancellationToken cancellationToken)
    {
        using var policyResponse = await SendAsync(
            HttpMethod.Post,
            $"v1/sys/policies/acl/{Escape(policyName)}",
            new { policy },
            cancellationToken);
        await EnsureSuccessAsync(policyResponse, $"create policy '{policyName}'", cancellationToken);

        using var tokenResponse = await SendAsync(
            HttpMethod.Post,
            ProvisionedTokenCreationPath,
            new
            {
                policies = new[] { policyName },
                no_default_policy = true,
                renewable = true,
                ttl = "720h",
                explicit_max_ttl = "2160h",
                display_name = displayName
            },
            cancellationToken);
        await EnsureSuccessAsync(tokenResponse, $"create token for policy '{policyName}'", cancellationToken);
        using var document = await ReadJsonAsync(tokenResponse, cancellationToken);
        return RequiredString(RequiredProperty(document.RootElement, "auth"), "client_token");
    }

    public async Task RevokeBootstrapTokenAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "v1/auth/token/revoke-self",
            new { },
            cancellationToken);
        await EnsureSuccessAsync(response, "revoke the bootstrap root token", cancellationToken);
    }

    private string KeyPath => string.Join(
        '/',
        "v1",
        Escape(_options.TransitMount),
        "keys",
        Escape(_options.KeyName));

    private string ProvisioningPolicyName =>
        $"{_options.RuntimePolicyName}-provisioning";

    private string ProvisioningTokenRoleName =>
        $"{_options.RuntimePolicyName}-provisioning";

    private string ProvisionedTokenCreationPath =>
        $"v1/auth/token/create/{Escape(ProvisioningTokenRoleName)}";

    private OpenBaoTransitKey ParseKey(
        JsonElement root)
    {
        var data = RequiredProperty(root, "data");
        var keyType = RequiredString(data, "type");

        if (!string.Equals(
                keyType,
                "rsa-3072",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The configured OpenBao Transit key is not RSA-3072.");
        }

        if (RequiredBoolean(data, "exportable")
            || RequiredBoolean(
                data,
                "allow_plaintext_backup"))
        {
            throw new InvalidOperationException(
                "The OpenBao Transit key permits private-key extraction.");
        }

        var latestVersion = RequiredInt32(
            data,
            "latest_version");

        var keys = RequiredProperty(data, "keys")
            .EnumerateObject()
            .Select(ParseKeyVersion)
            .OrderBy(version => version.Version)
            .ToArray();

        if (keys.Length == 0
            || keys[^1].Version != latestVersion)
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned an inconsistent key version set.");
        }

        return new OpenBaoTransitKey(
            latestVersion,
            keys);
    }

    private static OpenBaoTransitKeyVersion
        ParseKeyVersion(
            JsonProperty property)
    {
        if (!int.TryParse(
                property.Name,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var version)
            || version < 1)
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned an invalid key version.");
        }

        var publicKey = RequiredString(
            property.Value,
            "public_key");

        var creationTimeValue = RequiredString(
            property.Value,
            "creation_time");

        if (!DateTimeOffset.TryParse(
                creationTimeValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var creationTime))
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned an invalid key creation time.");
        }

        return new OpenBaoTransitKeyVersion(
            version,
            publicKey,
            creationTime.ToUniversalTime());
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            path);

        request.Headers.Add(
            "X-Vault-Token",
            _options.BootstrapToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The OpenBao bootstrap request timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "The OpenBao bootstrap request could not be completed.",
                exception);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
        => await OpenBaoAdminRetry.EnsureSuccessAsync(
            response,
            operation,
            cancellationToken);

    private static async Task<JsonDocument>
        ReadJsonAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        try
        {
            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "OpenBao returned malformed JSON.",
                exception);
        }
    }

    private static JsonElement RequiredProperty(
        JsonElement element,
        string name)
    {
        return element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidOperationException(
                $"OpenBao response is missing '{name}'.");
    }

    private static string RequiredString(
        JsonElement element,
        string name)
    {
        var value = RequiredProperty(element, name);

        return value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(
                value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException(
                $"OpenBao response contains an invalid '{name}'.");
    }

    private static bool RequiredBoolean(
        JsonElement element,
        string name)
    {
        var value = RequiredProperty(element, name);

        return value.ValueKind is
            JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException(
                $"OpenBao response contains an invalid '{name}'.");
    }

    private static int RequiredInt32(
        JsonElement element,
        string name)
    {
        var value = RequiredProperty(element, name);

        return value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException(
                $"OpenBao response contains an invalid '{name}'.");
    }

    private static string Escape(string value) =>
        Uri.EscapeDataString(value);
}

public sealed record OpenBaoTransitKey(
    int LatestVersion,
    IReadOnlyList<OpenBaoTransitKeyVersion> Versions);

public sealed record OpenBaoTransitKeyVersion(
    int Version,
    string PublicKeyPem,
    DateTimeOffset CreatedAtUtc);
