using System.Net;
using System.Net.Http.Json;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class OpenBaoRuntimePolicyVerifier
{
    public static async Task VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        var options = OpenBaoToolOptions
            .FromEnvironment();

        var runtimeToken = await File.ReadAllTextAsync(
            options.RuntimeTokenFile,
            cancellationToken);

        runtimeToken = runtimeToken.Trim();

        if (string.IsNullOrWhiteSpace(runtimeToken))
        {
            throw new InvalidOperationException(
                "The OpenBao runtime token file is empty. Run 'keys init' first.");
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = options.Address,
            Timeout = options.RequestTimeout
        };

        httpClient.DefaultRequestHeaders.Add(
            "X-Vault-Token",
            runtimeToken);

        var mount = Escape(options.TransitMount);
        var key = Escape(options.KeyName);
        var databaseMount = Escape(
            options.DatabaseMount);
        var databaseConnection = Escape(
            options.DatabaseConnectionName);
        var databaseRole = Escape(
            options.DatabaseStaticRoleName);

        await ExpectSuccessAsync(
            httpClient,
            HttpMethod.Post,
            $"v1/{mount}/sign/{key}/sha2-256",
            new
            {
                input = Convert.ToBase64String(
                    "runtime-policy-check"u8),
                prehashed = false,
                signature_algorithm = "pkcs1v15"
            },
            "sign with the configured JWT key",
            cancellationToken);

        await ExpectSuccessAsync(
            httpClient,
            HttpMethod.Get,
            $"v1/{databaseMount}/static-creds/{databaseRole}",
            null,
            "retrieve the configured runtime database credential",
            cancellationToken);

        var deniedRequests = new[]
        {
            new DeniedRequest(
                HttpMethod.Get,
                $"v1/{mount}/keys/{key}",
                null,
                "read signing-key configuration or public material"),

            new DeniedRequest(
                HttpMethod.Get,
                $"v1/{mount}/export/signing-key/{key}",
                null,
                "export signing-key material"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{mount}/keys/{key}/rotate",
                new { },
                "rotate the signing key"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{mount}/keys/{key}/config",
                new { min_decryption_version = 0 },
                "update signing-key configuration"),

            new DeniedRequest(
                HttpMethod.Delete,
                $"v1/{mount}/keys/{key}",
                null,
                "delete the signing key"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{mount}/keys/{key}-unrelated",
                new { type = "rsa-3072" },
                "create an arbitrary Transit key"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{mount}/sign/{key}-unrelated/sha2-256",
                new
                {
                    input = Convert.ToBase64String(
                        "unrelated-key-check"u8),
                    signature_algorithm = "pkcs1v15"
                },
                "sign with an unrelated key"),

            new DeniedRequest(
                HttpMethod.Get,
                $"v1/{databaseMount}/static-creds/{databaseRole}-migrator",
                null,
                "retrieve an unrelated or migrator database credential"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{databaseMount}/config/{databaseConnection}",
                new { },
                "configure the database secrets engine"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{databaseMount}/static-roles/{databaseRole}",
                new { },
                "change the runtime static-role configuration"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{databaseMount}/rotate-role/{databaseRole}",
                new { },
                "rotate the runtime database credential"),

            new DeniedRequest(
                HttpMethod.Post,
                $"v1/{databaseMount}/rotate-root/{databaseConnection}",
                new { },
                "rotate the database management credential"),

            new DeniedRequest(
                HttpMethod.Get,
                "v1/sys/mounts",
                null,
                "access OpenBao system administration")
        };

        foreach (var denied in deniedRequests)
        {
            await ExpectForbiddenAsync(
                httpClient,
                denied,
                cancellationToken);
        }
    }

    private static async Task ExpectSuccessAsync(
        HttpClient httpClient,
        HttpMethod method,
        string path,
        object? body,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            httpClient,
            method,
            path,
            body,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The runtime API identity could not {operation}; HTTP status {(int)response.StatusCode}.");
        }
    }

    private static async Task ExpectForbiddenAsync(
        HttpClient httpClient,
        DeniedRequest denied,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            httpClient,
            denied.Method,
            denied.Path,
            denied.Body,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"The runtime API identity was expected to be forbidden from attempting to {denied.Operation}, but OpenBao returned HTTP status {(int)response.StatusCode}.");
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            return await httpClient.SendAsync(
                request,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The OpenBao runtime-policy check timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "The OpenBao runtime-policy check could not be completed.",
                exception);
        }
    }

    private static string Escape(string value) =>
        Uri.EscapeDataString(value);

    private sealed record DeniedRequest(
        HttpMethod Method,
        string Path,
        object? Body,
        string Operation);
}
