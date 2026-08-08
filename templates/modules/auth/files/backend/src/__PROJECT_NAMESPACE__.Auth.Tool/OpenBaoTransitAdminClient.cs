using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

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
            "v1/auth/token/create",
            new
            {
                policies = new[]
                {
                    _options.RuntimePolicyName
                },
                no_default_policy = true,
                no_parent = true,
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

    private string KeyPath => string.Join(
        '/',
        "v1",
        Escape(_options.TransitMount),
        "keys",
        Escape(_options.KeyName));

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
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await response.Content.LoadIntoBufferAsync(
            cancellationToken);

        throw new InvalidOperationException(
            $"OpenBao could not {operation}; HTTP status {(int)response.StatusCode}.");
    }

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
