using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public sealed class OpenBaoDatabaseAdminClient(
    HttpClient httpClient,
    OpenBaoToolOptions openBaoOptions,
    DatabaseBootstrapOptions databaseOptions)
{
    private readonly HttpClient _httpClient = httpClient
        ?? throw new ArgumentNullException(
            nameof(httpClient));

    private readonly OpenBaoToolOptions _openBaoOptions =
        openBaoOptions
        ?? throw new ArgumentNullException(
            nameof(openBaoOptions));

    private readonly DatabaseBootstrapOptions
        _databaseOptions = databaseOptions
        ?? throw new ArgumentNullException(
            nameof(databaseOptions));

    public async Task ConfigureAsync(
        PostgresManagementCredential credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        await EnsureDatabaseEngineEnabledAsync(
            cancellationToken);

        using var configureResponse = await SendAsync(
            HttpMethod.Post,
            $"v1/{Mount}/config/{ConnectionName}",
            new
            {
                plugin_name =
                    "postgresql-database-plugin",
                allowed_roles = new[]
                {
                    _databaseOptions.StaticRoleName
                },
                connection_url =
                    $"host={_databaseOptions.Host} port={_databaseOptions.Port} dbname={_databaseOptions.DatabaseName} user={{{{username}}}} password={{{{password}}}} sslmode=disable",
                username = credential.Username,
                password = credential.Password,
                password_authentication =
                    "scram-sha-256",
                max_open_connections = 4,
                max_idle_connections = 2,
                max_connection_lifetime = "30s"
            },
            cancellationToken);

        await EnsureSuccessAsync(
            configureResponse,
            "configure the PostgreSQL database connection",
            cancellationToken);

        // OpenBao replaces the short-lived bootstrap value
        // above and becomes the sole owner of the management
        // role's current password.
        using var rotateRootResponse = await SendAsync(
            HttpMethod.Post,
            $"v1/{Mount}/rotate-root/{ConnectionName}",
            new { },
            cancellationToken);

        await EnsureSuccessAsync(
            rotateRootResponse,
            "rotate the PostgreSQL management credential",
            cancellationToken);

        using var staticRoleResponse = await SendAsync(
            HttpMethod.Post,
            $"v1/{Mount}/static-roles/{StaticRoleName}",
            new
            {
                db_name =
                    _databaseOptions.ConnectionName,
                username =
                    _databaseOptions.RuntimeUsername,
                credential_type = "password",
                rotation_period =
                    _databaseOptions.RotationPeriod,
                rotation_statements = new[]
                {
                    "ALTER ROLE \"{{name}}\" WITH PASSWORD '{{password}}'"
                }
            },
            cancellationToken);

        await EnsureSuccessAsync(
            staticRoleResponse,
            "configure the runtime PostgreSQL static role",
            cancellationToken);
    }

    public async Task RotateRuntimeRoleAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"v1/{Mount}/rotate-role/{StaticRoleName}",
            new { },
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            "rotate the runtime PostgreSQL credential",
            cancellationToken);
    }

    public async Task<OpenBaoStaticCredential>
        ReadRuntimeCredentialAsync(
            CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"v1/{Mount}/static-creds/{StaticRoleName}",
            null,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            "read the runtime PostgreSQL credential",
            cancellationToken);

        StaticCredentialResponse? result;

        try
        {
            result = await response.Content
                .ReadFromJsonAsync<
                    StaticCredentialResponse>(
                    cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "OpenBao returned malformed runtime database credential JSON.",
                exception);
        }

        var username = result?.Data?.Username;
        var password = result?.Data?.Password;

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "OpenBao returned an incomplete runtime database credential.");
        }

        return new OpenBaoStaticCredential(
            username,
            password);
    }

    private async Task EnsureDatabaseEngineEnabledAsync(
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

        using var document = await response.Content
            .ReadFromJsonAsync<JsonDocument>(
                cancellationToken)
            ?? throw new InvalidOperationException(
                "OpenBao returned an empty mount response.");

        var data = document.RootElement
            .GetProperty("data");

        if (data.TryGetProperty(
                $"{_databaseOptions.SecretsMount}/",
                out var mount))
        {
            if (mount.GetProperty("type")
                .GetString() != "database")
            {
                throw new InvalidOperationException(
                    $"OpenBao mount '{_databaseOptions.SecretsMount}' is not a Database secrets engine.");
            }

            return;
        }

        using var createResponse = await SendAsync(
            HttpMethod.Post,
            $"v1/sys/mounts/{Mount}",
            new
            {
                type = "database",
                description =
                    "Development PostgreSQL runtime credential rotation"
            },
            cancellationToken);

        await EnsureSuccessAsync(
            createResponse,
            "enable the Database secrets engine",
            cancellationToken);
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
            _openBaoOptions.BootstrapToken);

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
                "The OpenBao database administration request timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "The OpenBao database administration request could not be completed.",
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

    private string Mount => Escape(
        _databaseOptions.SecretsMount);

    private string ConnectionName => Escape(
        _databaseOptions.ConnectionName);

    private string StaticRoleName => Escape(
        _databaseOptions.StaticRoleName);

    private static string Escape(string value) =>
        Uri.EscapeDataString(value);

    private sealed record StaticCredentialResponse(
        [property: JsonPropertyName("data")]
        StaticCredentialData? Data);

    private sealed record StaticCredentialData(
        [property: JsonPropertyName("username")]
        string? Username,

        [property: JsonPropertyName("password")]
        string? Password);
}

public sealed record OpenBaoStaticCredential(
    string Username,
    string Password);
