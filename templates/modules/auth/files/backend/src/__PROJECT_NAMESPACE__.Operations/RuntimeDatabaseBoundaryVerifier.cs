using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace __PROJECT_NAMESPACE__.Operations;

public static class RuntimeDatabaseBoundaryVerifier
{
    public static async Task VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        var openBaoOptions = OpenBaoToolOptions
            .FromEnvironment(string.Empty);
        var databaseOptions = DatabaseBootstrapOptions
            .FromEnvironment();

        var credential = await ReadCredentialAsync(
            openBaoOptions,
            cancellationToken);

        await using var connection =
            new NpgsqlConnection(
                CreateConnectionString(
                    databaseOptions,
                    credential));

        await connection.OpenAsync(cancellationToken);

        await VerifyRoleAttributesAsync(
            connection,
            databaseOptions.RuntimeUsername,
            cancellationToken);

        await VerifyCrudAsync(
            connection,
            cancellationToken);

        await ExpectInsufficientPrivilegeAsync(
            connection,
            "CREATE TABLE public.runtime_boundary_forbidden (id integer)",
            "create a schema object",
            cancellationToken);

        await ExpectInsufficientPrivilegeAsync(
            connection,
            "CREATE ROLE runtime_boundary_forbidden NOLOGIN",
            "create a PostgreSQL role",
            cancellationToken);
    }

    private static async Task VerifyRoleAttributesAsync(
        NpgsqlConnection connection,
        string expectedUsername,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls FROM pg_roles WHERE rolname = current_user";

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The connected PostgreSQL runtime role was not found.");
        }

        if (!string.Equals(
                reader.GetString(0),
                expectedUsername,
                StringComparison.Ordinal)
            || reader.GetBoolean(1)
            || reader.GetBoolean(2)
            || reader.GetBoolean(3)
            || reader.GetBoolean(4)
            || reader.GetBoolean(5))
        {
            throw new InvalidOperationException(
                "The PostgreSQL runtime identity has unexpected administrative role attributes.");
        }
    }

    private static async Task VerifyCrudAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        var roleId = Guid.NewGuid();
        var suffix = roleId.ToString("N");
        var timestamp = DateTimeOffset.UtcNow;

        await using (var insert =
            connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO auth_roles (\"Id\", \"Name\", \"NormalizedName\", \"DisplayName\", \"IsSystem\", \"IsActive\", \"CreatedAtUtc\", \"UpdatedAtUtc\") VALUES ($1, $2, $3, $4, false, true, $5, $5)";
            insert.Parameters.AddWithValue(roleId);
            insert.Parameters.AddWithValue(
                $"boundary-{suffix}");
            insert.Parameters.AddWithValue(
                $"BOUNDARY-{suffix}");
            insert.Parameters.AddWithValue(
                "Runtime boundary verification");
            insert.Parameters.AddWithValue(timestamp);

            if (await insert.ExecuteNonQueryAsync(
                    cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The runtime credential could not insert application data.");
            }
        }

        await using (var select =
            connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                "SELECT COUNT(*) FROM auth_roles WHERE \"Id\" = $1";
            select.Parameters.AddWithValue(roleId);

            if ((long)(await select.ExecuteScalarAsync(
                    cancellationToken))! != 1)
            {
                throw new InvalidOperationException(
                    "The runtime credential could not read application data.");
            }
        }

        await using (var update =
            connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE auth_roles SET \"DisplayName\" = $2, \"UpdatedAtUtc\" = $3 WHERE \"Id\" = $1";
            update.Parameters.AddWithValue(roleId);
            update.Parameters.AddWithValue(
                "Runtime boundary updated");
            update.Parameters.AddWithValue(
                timestamp.AddSeconds(1));

            if (await update.ExecuteNonQueryAsync(
                    cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The runtime credential could not update application data.");
            }
        }

        await using (var delete =
            connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM auth_roles WHERE \"Id\" = $1";
            delete.Parameters.AddWithValue(roleId);

            if (await delete.ExecuteNonQueryAsync(
                    cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The runtime credential could not delete application data.");
            }
        }

        await transaction.RollbackAsync(
            cancellationToken);
    }

    private static async Task
        ExpectInsufficientPrivilegeAsync(
            NpgsqlConnection connection,
            string statement,
            string operation,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = statement;

        try
        {
            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState ==
                PostgresErrorCodes
                    .InsufficientPrivilege)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The runtime credential was unexpectedly able to {operation}.");
    }

    private static async Task<OpenBaoStaticCredential>
        ReadCredentialAsync(
            OpenBaoToolOptions options,
            CancellationToken cancellationToken)
    {
        // The HTTP header boundary requires an immutable string; the reader
        // minimizes that limitation to one decoded string and clears file bytes.
        var token = await SecretTextFileReader.ReadAsync(
            options.RuntimeTokenFile,
            "The OpenBao runtime token file is empty. Run 'auth init' first.",
            cancellationToken);

        using var httpClient = new HttpClient
        {
            BaseAddress = options.Address,
            Timeout = options.RequestTimeout
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"v1/{Escape(options.DatabaseMount)}/static-creds/{Escape(options.DatabaseStaticRoleName)}");

        request.Headers.Add("X-Vault-Token", token);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The runtime identity could not retrieve its database credential; HTTP status {(int)response.StatusCode}.");
        }

        CredentialResponse? result;

        try
        {
            result = await response.Content
                .ReadFromJsonAsync<CredentialResponse>(
                    cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "OpenBao returned malformed runtime database credential JSON.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(
                result?.Data?.Username)
            || string.IsNullOrWhiteSpace(
                result.Data.Password))
        {
            throw new InvalidOperationException(
                "OpenBao returned an incomplete runtime database credential.");
        }

        return new OpenBaoStaticCredential(
            result.Data.Username,
            result.Data.Password);
    }

    private static string CreateConnectionString(
        DatabaseBootstrapOptions options,
        OpenBaoStaticCredential credential) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.DatabaseName,
            Username = credential.Username,
            Password = credential.Password,
            Pooling = false,
            GssEncryptionMode =
                GssEncryptionMode.Disable,
            IncludeErrorDetail = false
        }.ConnectionString;

    private static string Escape(string value) =>
        Uri.EscapeDataString(value);

    private sealed record CredentialResponse(
        [property: JsonPropertyName("data")]
        CredentialData? Data);

    private sealed record CredentialData(
        [property: JsonPropertyName("username")]
        string? Username,

        [property: JsonPropertyName("password")]
        string? Password);
}
