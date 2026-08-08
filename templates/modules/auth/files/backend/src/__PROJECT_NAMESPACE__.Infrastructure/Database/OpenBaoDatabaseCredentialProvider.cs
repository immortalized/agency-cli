using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Database;

public sealed class OpenBaoDatabaseCredentialProvider
    : IDatabaseCredentialProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _credentialPath;

    public OpenBaoDatabaseCredentialProvider(
        HttpClient httpClient,
        IOptions<OpenBaoDatabaseCredentialOptions>
            options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;

        var value = options.Value;

        _credentialPath = string.Join(
            '/',
            "v1",
            Uri.EscapeDataString(
                value.SecretsMount),
            "static-creds",
            Uri.EscapeDataString(
                value.StaticRoleName));

        _httpClient.DefaultRequestHeaders.Add(
            "X-Vault-Token",
            ReadToken(value.TokenFile));
    }

    public async Task<DatabaseCredential>
        GetCredentialAsync(
            CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            _credentialPath);

        using var response = await SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"OpenBao database credential retrieval failed with HTTP status {(int)response.StatusCode}.");
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
                "OpenBao returned malformed database credential JSON.",
                exception);
        }

        var username = result?.Data?.Username;
        var password = result?.Data?.Password;

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "OpenBao returned an incomplete database credential.");
        }

        return new DatabaseCredential(
            username,
            password);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
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
                "OpenBao database credential retrieval timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "OpenBao database credential retrieval could not be completed.",
                exception);
        }
    }

    private static string ReadToken(
        string tokenFile)
    {
        try
        {
            var token = File.ReadAllText(
                    tokenFile)
                .Trim();

            return string.IsNullOrWhiteSpace(token)
                ? throw new InvalidOperationException(
                    "The OpenBao runtime token file is empty.")
                : token;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The OpenBao runtime token file could not be read.",
                exception);
        }
    }

    private sealed record CredentialResponse(
        [property: JsonPropertyName("data")]
        CredentialData? Data);

    private sealed record CredentialData(
        [property: JsonPropertyName("username")]
        string? Username,

        [property: JsonPropertyName("password")]
        string? Password);
}
