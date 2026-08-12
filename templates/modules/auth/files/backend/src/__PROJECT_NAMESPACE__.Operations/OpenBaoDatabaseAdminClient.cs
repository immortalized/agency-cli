using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace __PROJECT_NAMESPACE__.Operations;

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

    public async Task<IReadOnlyList<string>>
        GetMissingConfigurationAsync(
            CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        using var mountsResponse = await SendAdminAsync(
            HttpMethod.Get,
            "v1/sys/mounts",
            null,
            "read enabled secrets engines",
            cancellationToken);
        using var mounts = await mountsResponse.Content
            .ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new InvalidOperationException(
                "OpenBao returned an empty mount response.");
        var data = mounts.RootElement.GetProperty("data");

        if (!data.TryGetProperty(
                $"{_databaseOptions.SecretsMount}/",
                out var mount))
        {
            missing.Add($"{_databaseOptions.SecretsMount}/ mount");
            missing.Add($"{_databaseOptions.SecretsMount}/config/{_databaseOptions.ConnectionName}");
            missing.Add($"{_databaseOptions.SecretsMount}/static-roles/{_databaseOptions.StaticRoleName}");
            missing.Add($"{_databaseOptions.SecretsMount}/static-roles/{_databaseOptions.MigratorStaticRoleName}");
            return missing;
        }

        if (mount.GetProperty("type").GetString() != "database")
        {
            throw new InvalidOperationException(
                $"OpenBao mount '{_databaseOptions.SecretsMount}' is not a Database secrets engine.");
        }

        await AddIfMissingAsync(
            $"v1/{Mount}/config/{ConnectionName}",
            $"{_databaseOptions.SecretsMount}/config/{_databaseOptions.ConnectionName}",
            missing,
            cancellationToken);
        await AddIfMissingAsync(
            $"v1/{Mount}/static-roles/{StaticRoleName}",
            $"{_databaseOptions.SecretsMount}/static-roles/{_databaseOptions.StaticRoleName}",
            missing,
            cancellationToken);
        await AddIfMissingAsync(
            $"v1/{Mount}/static-roles/{MigratorStaticRoleName}",
            $"{_databaseOptions.SecretsMount}/static-roles/{_databaseOptions.MigratorStaticRoleName}",
            missing,
            cancellationToken);

        return missing;
    }

    public async Task ConfigureAsync(
        PostgresManagementCredential credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        await EnsureDatabaseEngineEnabledAsync(
            cancellationToken);

        using var configureResponse = await SendAdminAsync(
            HttpMethod.Post,
            $"v1/{Mount}/config/{ConnectionName}",
            new
            {
                plugin_name =
                    "postgresql-database-plugin",
                allowed_roles = new[]
                {
                    _databaseOptions.StaticRoleName,
                    _databaseOptions.MigratorStaticRoleName
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
            "configure the PostgreSQL database connection",
            cancellationToken);

        // OpenBao replaces the short-lived bootstrap value
        // above and becomes the sole owner of the management
        // role's current password.
        using var rotateRootResponse = await SendAdminAsync(
            HttpMethod.Post,
            $"v1/{Mount}/rotate-root/{ConnectionName}",
            new { },
            "rotate the PostgreSQL management credential",
            cancellationToken);

        using var staticRoleResponse = await SendAdminAsync(
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
            "configure the runtime PostgreSQL static role",
            cancellationToken);

        using var migratorRoleResponse = await SendAdminAsync(
            HttpMethod.Post,
            $"v1/{Mount}/static-roles/{MigratorStaticRoleName}",
            new
            {
                db_name = _databaseOptions.ConnectionName,
                username = _databaseOptions.MigratorUsername,
                credential_type = "password",
                rotation_period = _databaseOptions.RotationPeriod,
                rotation_statements = new[]
                {
                    "ALTER ROLE \"{{name}}\" WITH PASSWORD '{{password}}'"
                }
            },
            "configure the migrator PostgreSQL static role",
            cancellationToken);

        await RotateStaticRoleAsync(
            StaticRoleName,
            "take ownership of the runtime PostgreSQL credential",
            cancellationToken);

        await RotateStaticRoleAsync(
            MigratorStaticRoleName,
            "take ownership of the migrator PostgreSQL credential",
            cancellationToken);
    }

    public async Task RotateRuntimeRoleAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Post,
            $"v1/{Mount}/rotate-role/{StaticRoleName}",
            new { },
            "rotate the runtime PostgreSQL credential",
            cancellationToken);
    }

    public async Task<OpenBaoStaticCredential>
        ReadRuntimeCredentialAsync(
            CancellationToken cancellationToken)
        => await ReadStaticCredentialAsync(
            StaticRoleName,
            "runtime",
            cancellationToken);

    public async Task<OpenBaoStaticCredential>
        ReadMigratorCredentialAsync(
            CancellationToken cancellationToken)
        => await ReadStaticCredentialAsync(
            MigratorStaticRoleName,
            "migrator",
            cancellationToken);

    private async Task<OpenBaoStaticCredential>
        ReadStaticCredentialAsync(
            string roleName,
            string purpose,
            CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Get,
            $"v1/{Mount}/static-creds/{roleName}",
            null,
            $"read the {purpose} PostgreSQL credential",
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
                $"OpenBao returned an incomplete {purpose} database credential.");
        }

        return new OpenBaoStaticCredential(
            username,
            password);
    }

    private async Task RotateStaticRoleAsync(
        string roleName,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Post,
            $"v1/{Mount}/rotate-role/{roleName}",
            new { },
            operation,
            cancellationToken);
    }

    private async Task AddIfMissingAsync(
        string path,
        string description,
        ICollection<string> missing,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Get,
            path,
            null,
            $"read provisioning state '{description}'",
            cancellationToken,
            allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            missing.Add(description);
            return;
        }
    }

    private async Task EnsureDatabaseEngineEnabledAsync(
        CancellationToken cancellationToken)
    {
        await OpenBaoAdminRetry.ExecuteAsync(
            EnsureDatabaseEngineEnabledOnceAsync,
            "enable the Database secrets engine while Raft leadership settles",
            cancellationToken);
    }

    private async Task EnsureDatabaseEngineEnabledOnceAsync(
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
                    "OpenBao-managed runtime and migrator PostgreSQL credentials"
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

    private async Task<HttpResponseMessage> SendAdminAsync(
        HttpMethod method,
        string path,
        object? body,
        string operation,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        return await OpenBaoAdminRetry.ExecuteAsync(
            async retryCancellationToken =>
            {
                var response = await SendAsync(
                    method,
                    path,
                    body,
                    retryCancellationToken);
                try
                {
                    if (!allowNotFound
                        || response.StatusCode != HttpStatusCode.NotFound)
                    {
                        await EnsureSuccessAsync(
                            response,
                            operation,
                            retryCancellationToken);
                    }
                    return response;
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            },
            $"{operation} while Raft leadership settles",
            cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
        => await OpenBaoAdminRetry.EnsureSuccessAsync(
            response,
            operation,
            cancellationToken);

    private string Mount => Escape(
        _databaseOptions.SecretsMount);

    private string ConnectionName => Escape(
        _databaseOptions.ConnectionName);

    private string StaticRoleName => Escape(
        _databaseOptions.StaticRoleName);

    private string MigratorStaticRoleName => Escape(
        _databaseOptions.MigratorStaticRoleName);

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
