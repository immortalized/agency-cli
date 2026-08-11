using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace __PROJECT_NAMESPACE__.Operations;

public static class OpenBaoStaticCredentialReader
{
    public static async Task<OpenBaoStaticCredential> ReadAsync(
        OpenBaoToolOptions options,
        string tokenFile,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        // The HTTP header boundary requires an immutable string; the reader
        // minimizes that limitation to one decoded string and clears file bytes.
        var token = await SecretTextFileReader.ReadAsync(
            tokenFile,
            "The OpenBao capability token file is empty.",
            cancellationToken);

        using var client = new HttpClient
        {
            BaseAddress = options.Address,
            Timeout = options.RequestTimeout
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"v1/{Uri.EscapeDataString(options.DatabaseMount)}/static-creds/{Uri.EscapeDataString(roleName)}");
        request.Headers.Add("X-Vault-Token", token);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The supplied OpenBao identity could not retrieve role '{roleName}'; HTTP status {(int)response.StatusCode}.");
        }

        var result = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);
        if (string.IsNullOrWhiteSpace(result?.Data?.Username)
            || string.IsNullOrWhiteSpace(result.Data.Password))
        {
            throw new InvalidOperationException("OpenBao returned an incomplete database credential.");
        }
        return new OpenBaoStaticCredential(result.Data.Username, result.Data.Password);
    }

    private sealed record Response(
        [property: JsonPropertyName("data")] Data? Data);

    private sealed record Data(
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("password")] string? Password);
}
