using Npgsql;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class OpenBaoDatabaseRotationVerifier
{
    public static async Task RotateAndVerifyAsync(
        CancellationToken cancellationToken = default)
    {
        var openBaoOptions = OpenBaoToolOptions
            .FromEnvironment();
        var databaseOptions = DatabaseBootstrapOptions
            .FromEnvironment();

        using var httpClient = new HttpClient
        {
            BaseAddress = openBaoOptions.Address,
            Timeout = openBaoOptions.RequestTimeout
        };

        var client = new OpenBaoDatabaseAdminClient(
            httpClient,
            openBaoOptions,
            databaseOptions);

        var previous = await client
            .ReadRuntimeCredentialAsync(
                cancellationToken);

        await VerifyConnectionAsync(
            databaseOptions,
            previous,
            shouldSucceed: true,
            cancellationToken);

        await client.RotateRuntimeRoleAsync(
            cancellationToken);

        var current = await client
            .ReadRuntimeCredentialAsync(
                cancellationToken);

        if (!string.Equals(
                previous.Username,
                current.Username,
                StringComparison.Ordinal)
            || string.Equals(
                previous.Password,
                current.Password,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OpenBao did not return the expected rotated static credential.");
        }

        await VerifyConnectionAsync(
            databaseOptions,
            current,
            shouldSucceed: true,
            cancellationToken);

        await VerifyConnectionAsync(
            databaseOptions,
            previous,
            shouldSucceed: false,
            cancellationToken);
    }

    private static async Task VerifyConnectionAsync(
        DatabaseBootstrapOptions options,
        OpenBaoStaticCredential credential,
        bool shouldSucceed,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(
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
                }.ConnectionString);

        try
        {
            await connection.OpenAsync(
                cancellationToken);

            if (!shouldSucceed)
            {
                throw new InvalidOperationException(
                    "The previous runtime database password still authenticated after rotation.");
            }
        }
        catch (PostgresException exception)
            when (!shouldSucceed
                && exception.SqlState ==
                    PostgresErrorCodes
                        .InvalidPassword)
        {
            return;
        }
    }
}
