using System.Security.Cryptography;
using Npgsql;

namespace __PROJECT_NAMESPACE__.Operations;

public static class PostgresRoleBootstrapper
{
    public static async Task RetireBootstrapCredentialAsync(
        DatabaseBootstrapOptions options,
        bool allowAlreadyRetired = false,
        CancellationToken cancellationToken = default)
    {
        if (!options.RetireBootstrapCredential)
        {
            return;
        }

        var password = options.ReadBootstrapPassword();
        await using var connection = new NpgsqlConnection(
            CreateConnectionString(
                options,
                options.BootstrapUsername,
                password));
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (allowAlreadyRetired
                && exception.SqlState is
                    PostgresErrorCodes.InvalidPassword
                    or "28000")
        {
            return;
        }
        await ExecuteAsync(
            connection,
            $"ALTER ROLE {QuoteIdentifier(options.BootstrapUsername)} NOLOGIN PASSWORD NULL",
            cancellationToken);
    }

    public static async Task<PostgresManagementCredential>
        BootstrapAsync(
            DatabaseBootstrapOptions options,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var managementPassword = GeneratePassword();
        var runtimeInitialPassword = GeneratePassword();
        var migratorInitialPassword =
            options.CreateInitialMigratorPassword();
        var bootstrapPassword =
            options.ReadBootstrapPassword();

        var (bootstrapConnection, migratedLegacy) =
            await OpenBootstrapConnectionAsync(
                options,
                bootstrapPassword,
                cancellationToken);

        await using (bootstrapConnection)
        {
            await ProvisionMigratorAsync(
                bootstrapConnection,
                options,
                migratorInitialPassword,
                cancellationToken);

            await ProvisionOpenBaoManagerAsync(
                bootstrapConnection,
                options,
                managementPassword,
                cancellationToken);

            await ProvisionRuntimeAsync(
                bootstrapConnection,
                options,
                runtimeInitialPassword,
                cancellationToken);

            await GrantRuntimePrivilegesAsync(
                bootstrapConnection,
                options,
                cancellationToken);

            if (migratedLegacy)
            {
                await DisableLegacyRoleAsync(
                    bootstrapConnection,
                    options,
                    cancellationToken);
            }
        }

        return new PostgresManagementCredential(
            options.OpenBaoManagerUsername,
            managementPassword);
    }

    private static async Task<(
        NpgsqlConnection Connection,
        bool MigratedLegacy)>
        OpenBootstrapConnectionAsync(
            DatabaseBootstrapOptions options,
            string bootstrapPassword,
            CancellationToken cancellationToken)
    {
        var bootstrap = new NpgsqlConnection(
            CreateConnectionString(
                options,
                options.BootstrapUsername,
                bootstrapPassword));

        try
        {
            await bootstrap.OpenAsync(cancellationToken);

            return (
                bootstrap,
                !string.Equals(
                    options.BootstrapUsername,
                    options.LegacyUsername,
                    StringComparison.Ordinal)
                && await RoleExistsAsync(
                    bootstrap,
                    options.LegacyUsername,
                    cancellationToken));
        }
        catch (PostgresException exception)
            when (exception.SqlState is
                PostgresErrorCodes.InvalidPassword
                or "28000")
        {
            await bootstrap.DisposeAsync();
        }

        var legacy = new NpgsqlConnection(
            CreateConnectionString(
                options,
                options.LegacyUsername,
                bootstrapPassword));

        try
        {
            await legacy.OpenAsync(cancellationToken);

            var bootstrapRole = QuoteIdentifier(
                options.BootstrapUsername);

            if (!await RoleExistsAsync(
                    legacy,
                    options.BootstrapUsername,
                    cancellationToken))
            {
                await ExecuteAsync(
                    legacy,
                    $"CREATE ROLE {bootstrapRole} LOGIN PASSWORD {QuoteLiteral(bootstrapPassword)} SUPERUSER CREATEDB CREATEROLE INHERIT NOREPLICATION NOBYPASSRLS",
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            await legacy.DisposeAsync();

            throw new InvalidOperationException(
                $"PostgreSQL bootstrap could not authenticate as either '{options.BootstrapUsername}' or the declared legacy role '{options.LegacyUsername}'. The database security migration did not proceed.",
                exception);
        }

        await legacy.DisposeAsync();

        var migratedBootstrap = new NpgsqlConnection(
            CreateConnectionString(
                options,
                options.BootstrapUsername,
                bootstrapPassword));

        await migratedBootstrap.OpenAsync(
            cancellationToken);

        return (migratedBootstrap, true);
    }

    private static async Task ProvisionMigratorAsync(
        NpgsqlConnection connection,
        DatabaseBootstrapOptions options,
        string initialPassword,
        CancellationToken cancellationToken)
    {
        var migrator = QuoteIdentifier(
            options.MigratorUsername);
        var password = QuoteLiteral(initialPassword);

        if (await RoleExistsAsync(
                connection,
                options.MigratorUsername,
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                $"ALTER ROLE {migrator} LOGIN PASSWORD {password} NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOREPLICATION NOBYPASSRLS",
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(
                connection,
                $"CREATE ROLE {migrator} LOGIN PASSWORD {password} NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOREPLICATION NOBYPASSRLS",
                cancellationToken);
        }

        await ExecuteAsync(
            connection,
            $"ALTER DATABASE {QuoteIdentifier(options.DatabaseName)} OWNER TO {migrator}",
            cancellationToken);

        await ExecuteAsync(
            connection,
            "REVOKE CREATE ON SCHEMA public FROM PUBLIC",
            cancellationToken);

        await ExecuteAsync(
            connection,
            $"ALTER SCHEMA public OWNER TO {migrator}",
            cancellationToken);
    }

    private static async Task
        ProvisionOpenBaoManagerAsync(
            NpgsqlConnection connection,
            DatabaseBootstrapOptions options,
            string managementPassword,
            CancellationToken cancellationToken)
    {
        var manager = QuoteIdentifier(
            options.OpenBaoManagerUsername);
        var statement = await RoleExistsAsync(
                connection,
                options.OpenBaoManagerUsername,
                cancellationToken)
            ? "ALTER ROLE"
            : "CREATE ROLE";

        await ExecuteAsync(
            connection,
            $"{statement} {manager} LOGIN PASSWORD {QuoteLiteral(managementPassword)} NOINHERIT NOSUPERUSER NOCREATEDB CREATEROLE NOREPLICATION NOBYPASSRLS",
            cancellationToken);

        await ExecuteAsync(
            connection,
            $"GRANT CONNECT ON DATABASE {QuoteIdentifier(options.DatabaseName)} TO {manager}",
            cancellationToken);
    }

    private static async Task ProvisionRuntimeAsync(
        NpgsqlConnection connection,
        DatabaseBootstrapOptions options,
        string initialPassword,
        CancellationToken cancellationToken)
    {
        var runtime = QuoteIdentifier(
            options.RuntimeUsername);

        if (await RoleExistsAsync(
                connection,
                options.RuntimeUsername,
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                $"ALTER ROLE {runtime} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOREPLICATION NOBYPASSRLS",
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(
                connection,
                $"CREATE ROLE {runtime} LOGIN PASSWORD {QuoteLiteral(initialPassword)} NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOREPLICATION NOBYPASSRLS",
                cancellationToken);
        }

        await ExecuteAsync(
            connection,
            $"GRANT {runtime} TO {QuoteIdentifier(options.OpenBaoManagerUsername)} WITH ADMIN OPTION",
            cancellationToken);

        await ExecuteAsync(
            connection,
            $"GRANT {QuoteIdentifier(options.MigratorUsername)} TO {QuoteIdentifier(options.OpenBaoManagerUsername)} WITH ADMIN OPTION",
            cancellationToken);
    }

    private static async Task GrantRuntimePrivilegesAsync(
        NpgsqlConnection connection,
        DatabaseBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        var database = QuoteIdentifier(
            options.DatabaseName);
        var runtime = QuoteIdentifier(
            options.RuntimeUsername);
        var migrator = QuoteIdentifier(
            options.MigratorUsername);

        var statements = new[]
        {
            $"REVOKE ALL ON DATABASE {database} FROM PUBLIC",
            $"GRANT CONNECT ON DATABASE {database} TO {runtime}",
            $"REVOKE ALL ON SCHEMA public FROM {runtime}",
            $"GRANT USAGE ON SCHEMA public TO {runtime}",
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {runtime}",
            $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {runtime}",
            $"ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {runtime}",
            $"ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {runtime}"
        };

        foreach (var statement in statements)
        {
            await ExecuteAsync(
                connection,
                statement,
                cancellationToken);
        }
    }

    private static async Task DisableLegacyRoleAsync(
        NpgsqlConnection connection,
        DatabaseBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        if (!await RoleExistsAsync(
                connection,
                options.LegacyUsername,
                cancellationToken))
        {
            return;
        }

        await TransferPublicObjectOwnershipAsync(
            connection,
            options,
            cancellationToken);

        var legacy = QuoteIdentifier(
            options.LegacyUsername);

        await ExecuteAsync(
            connection,
            $"REVOKE ALL ON DATABASE {QuoteIdentifier(options.DatabaseName)} FROM {legacy}",
            cancellationToken);

        await ExecuteAsync(
            connection,
            $"REVOKE ALL ON SCHEMA public FROM {legacy}",
            cancellationToken);

        await ExecuteAsync(
            connection,
            $"ALTER ROLE {legacy} NOLOGIN PASSWORD NULL",
            cancellationToken);
    }

    private static async Task
        TransferPublicObjectOwnershipAsync(
            NpgsqlConnection connection,
            DatabaseBootstrapOptions options,
            CancellationToken cancellationToken)
    {
        var objects = new List<(char Kind, string Name)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT c.relkind, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace JOIN pg_roles r ON r.oid = c.relowner WHERE n.nspname = 'public' AND r.rolname = $1 AND c.relkind IN ('r', 'p', 'S', 'v', 'm', 'f')";
            command.Parameters.AddWithValue(
                options.LegacyUsername);

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                cancellationToken))
            {
                objects.Add((
                    reader.GetChar(0),
                    reader.GetString(1)));
            }
        }

        var migrator = QuoteIdentifier(
            options.MigratorUsername);

        foreach (var (kind, name) in objects)
        {
            var objectType = kind switch
            {
                'S' => "SEQUENCE",
                'v' => "VIEW",
                'm' => "MATERIALIZED VIEW",
                'f' => "FOREIGN TABLE",
                _ => "TABLE"
            };

            await ExecuteAsync(
                connection,
                $"ALTER {objectType} public.{QuoteIdentifier(name)} OWNER TO {migrator}",
                cancellationToken);
        }
    }

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        Span<char> encoded = stackalloc char[64];
        try
        {
            // Npgsql requires an immutable password string that cannot be cleared;
            // build only that final string and clear both temporary buffers.
            if (!Convert.TryToBase64Chars(bytes, encoded, out var charactersWritten))
            {
                throw new InvalidOperationException("The PostgreSQL password could not be encoded.");
            }

            while (charactersWritten > 0 && encoded[charactersWritten - 1] == '=')
            {
                charactersWritten--;
            }

            for (var index = 0; index < charactersWritten; index++)
            {
                encoded[index] = encoded[index] switch
                {
                    '+' => '-',
                    '/' => '_',
                    var value => value
                };
            }

            return new string(encoded[..charactersWritten]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            encoded.Clear();
        }
    }

    private static async Task<bool> RoleExistsAsync(
        NpgsqlConnection connection,
        string roleName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1)";
        command.Parameters.AddWithValue(roleName);

        return (bool)(await command.ExecuteScalarAsync(
            cancellationToken))!;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string statement,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string CreateConnectionString(
        DatabaseBootstrapOptions options,
        string username,
        string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.DatabaseName,
            Username = username,
            Password = password,
            Pooling = false,
            GssEncryptionMode =
                GssEncryptionMode.Disable,
            IncludeErrorDetail = false
        }.ConnectionString;

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private static string QuoteLiteral(string value) =>
        $"'{value.Replace("'", "''")}'";
}

public sealed record PostgresManagementCredential(
    string Username,
    string Password);
