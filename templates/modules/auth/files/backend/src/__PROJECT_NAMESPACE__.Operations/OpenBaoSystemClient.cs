using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace __PROJECT_NAMESPACE__.Operations;

public sealed class OpenBaoSystemClient : IOpenBaoSystemClient, IDisposable
{
    private readonly HttpClient _httpClient;

    public OpenBaoSystemClient()
    {
        var options = OperationsEnvironment.FromEnvironment();
        _httpClient = new HttpClient
        {
            BaseAddress = options.OpenBaoAddress,
            Timeout = options.RequestTimeout
        };
    }

    internal OpenBaoSystemClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<OpenBaoSealStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 60; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    "v1/sys/seal-status",
                    cancellationToken);
                await EnsureSuccessAsync(response, "read seal status", cancellationToken);
                var value = await response.Content.ReadFromJsonAsync<SealStatusResponse>(
                    cancellationToken)
                    ?? throw new InvalidOperationException("OpenBao returned an empty seal status response.");

                return new OpenBaoSealStatus(
                    value.Initialized,
                    value.Sealed,
                    value.Shares,
                    value.Threshold,
                    value.Progress,
                    value.Type ?? "unknown");
            }
            catch (HttpRequestException) when (attempt < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw new InvalidOperationException("OpenBao did not become reachable within 60 seconds.");
    }

    public async Task<OpenBaoInitializationResult> InitializeAsync(
        int shares,
        int threshold,
        bool autoSeal,
        CancellationToken cancellationToken = default)
    {
        object body = autoSeal
            ? new { recovery_shares = 0, recovery_threshold = 0 }
            : new { secret_shares = shares, secret_threshold = threshold };

        using var response = await _httpClient.PostAsJsonAsync(
            "v1/sys/init",
            body,
            cancellationToken);
        await EnsureSuccessAsync(response, "initialize OpenBao", cancellationToken);
        var value = await response.Content.ReadFromJsonAsync<InitResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned an empty initialization response.");

        if (string.IsNullOrWhiteSpace(value.RootToken))
        {
            throw new InvalidOperationException("OpenBao did not return the initial root token.");
        }

        var material = (value.KeysBase64 ?? [])
            .Select(Encoding.UTF8.GetBytes)
            .ToArray();

        if (!autoSeal && material.Length != shares)
        {
            foreach (var item in material)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(item);
            }
            throw new InvalidOperationException("OpenBao returned an unexpected number of unseal shares.");
        }

        return new OpenBaoInitializationResult(material, value.RootToken);
    }

    public async Task<OpenBaoSealStatus> SubmitUnsealShareAsync(
        ReadOnlyMemory<byte> share,
        bool reset = false,
        CancellationToken cancellationToken = default)
    {
        using var response = reset
            ? await _httpClient.PostAsJsonAsync(
                "v1/sys/unseal",
                new { reset = true },
                cancellationToken)
            : await PostSensitiveJsonAsync(
                "v1/sys/unseal",
                CreateUnsealSharePayload(share.Span),
                cancellationToken);
        await EnsureSuccessAsync(response, "submit an unseal share", cancellationToken);
        var value = await response.Content.ReadFromJsonAsync<SealStatusResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned an empty unseal response.");
        return new OpenBaoSealStatus(
            value.Initialized,
            value.Sealed,
            value.Shares,
            value.Threshold,
            value.Progress,
            value.Type ?? "unknown");
    }

    public async Task<string> BeginRekeyAsync(
        int shares,
        int threshold,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "v1/sys/rotate/root/init",
            new
            {
                secret_shares = shares,
                secret_threshold = threshold,
                backup = false,
                require_verification = true
            },
            cancellationToken);
        await EnsureSuccessAsync(response, "begin native OpenBao rekey", cancellationToken);
        var value = await ReadRekeyResponseAsync(response, cancellationToken);
        return value.Nonce
            ?? throw new InvalidOperationException("OpenBao did not return a rekey nonce.");
    }

    public async Task<OpenBaoRekeyProgress> SubmitRekeyShareAsync(
        ReadOnlyMemory<byte> share,
        string nonce,
        CancellationToken cancellationToken = default)
    {
        using var response = await PostSensitiveJsonAsync(
            "v1/sys/rotate/root/update",
            CreateRekeySharePayload(share.Span, nonce),
            cancellationToken);
        await EnsureSuccessAsync(response, "submit a native OpenBao rekey share", cancellationToken);
        var value = await ReadRekeyResponseAsync(response, cancellationToken);
        return new OpenBaoRekeyProgress(
            value.Complete,
            value.VerificationRequired,
            value.Nonce ?? nonce,
            value.Required,
            (value.KeysBase64 ?? []).Select(Encoding.UTF8.GetBytes).ToArray());
    }

    public async Task<bool> SubmitRekeyVerificationShareAsync(
        ReadOnlyMemory<byte> share,
        string nonce,
        CancellationToken cancellationToken = default)
    {
        using var response = await PostSensitiveJsonAsync(
            "v1/sys/rotate/root/verify",
            CreateRekeySharePayload(share.Span, nonce),
            cancellationToken);
        await EnsureSuccessAsync(
            response,
            "verify a newly captured OpenBao rotation share",
            cancellationToken);
        var value = await ReadRekeyResponseAsync(response, cancellationToken);
        return value.Complete;
    }

    internal static byte[] CreateUnsealSharePayload(
        ReadOnlySpan<byte> share)
    {
        ReadOnlySpan<byte> prefix = "{\"key\":\""u8;
        ReadOnlySpan<byte> suffix = "\"}"u8;
        return CreateSensitivePayload(prefix, share, suffix);
    }

    internal static byte[] CreateRekeySharePayload(
        ReadOnlySpan<byte> share,
        string nonce)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        var encodedNonce = JsonEncodedText.Encode(nonce).EncodedUtf8Bytes;
        ReadOnlySpan<byte> prefix = "{\"key\":\""u8;
        ReadOnlySpan<byte> separator = "\",\"nonce\":\""u8;
        ReadOnlySpan<byte> suffix = "\"}"u8;
        var payload = GC.AllocateUninitializedArray<byte>(
            checked(prefix.Length
                + share.Length
                + separator.Length
                + encodedNonce.Length
                + suffix.Length));

        try
        {
            ValidateShareBytes(share);
            var destination = payload.AsSpan();
            prefix.CopyTo(destination);
            destination = destination[prefix.Length..];
            share.CopyTo(destination);
            destination = destination[share.Length..];
            separator.CopyTo(destination);
            destination = destination[separator.Length..];
            encodedNonce.CopyTo(destination);
            destination = destination[encodedNonce.Length..];
            suffix.CopyTo(destination);
            return payload;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(payload);
            throw;
        }
    }

    private static byte[] CreateSensitivePayload(
        ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> share,
        ReadOnlySpan<byte> suffix)
    {
        var payload = GC.AllocateUninitializedArray<byte>(
            checked(prefix.Length + share.Length + suffix.Length));
        try
        {
            ValidateShareBytes(share);
            var destination = payload.AsSpan();
            prefix.CopyTo(destination);
            destination = destination[prefix.Length..];
            share.CopyTo(destination);
            suffix.CopyTo(destination[share.Length..]);
            return payload;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(payload);
            throw;
        }
    }

    private static void ValidateShareBytes(ReadOnlySpan<byte> share)
    {
        if (share.IsEmpty)
        {
            throw new ArgumentException("An OpenBao share cannot be empty.", nameof(share));
        }

        // OpenBao's keys_base64 values use only JSON-string-safe ASCII. Rejecting
        // anything else keeps the byte-level writer correct if an unexpected share
        // format is supplied, without ever creating an immutable plaintext string.
        foreach (var value in share)
        {
            if (value < 0x20 || value >= 0x7f || value is (byte)'\"' or (byte)'\\')
            {
                throw new ArgumentException(
                    "An OpenBao share contains bytes that require JSON escaping.",
                    nameof(share));
            }
        }
    }

    private async Task<HttpResponseMessage> PostSensitiveJsonAsync(
        string path,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        ByteArrayContent? content = null;
        HttpRequestMessage? request = null;

        try
        {
            content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = content
            };
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        finally
        {
            if (request is not null)
            {
                request.Dispose();
            }
            else
            {
                content?.Dispose();
            }
            CryptographicOperations.ZeroMemory(payload);
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

    private static async Task<RekeyResponse> ReadRekeyResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;
        var payload = root.TryGetProperty("data", out var data) ? data : root;
        return payload.Deserialize<RekeyResponse>()
            ?? throw new InvalidOperationException("OpenBao returned an empty rotation response.");
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record SealStatusResponse(
        [property: JsonPropertyName("initialized")] bool Initialized,
        [property: JsonPropertyName("sealed")] bool Sealed,
        [property: JsonPropertyName("n")] int Shares,
        [property: JsonPropertyName("t")] int Threshold,
        [property: JsonPropertyName("progress")] int Progress,
        [property: JsonPropertyName("type")] string? Type);

    private sealed record InitResponse(
        [property: JsonPropertyName("keys_base64")] string[]? KeysBase64,
        [property: JsonPropertyName("root_token")] string? RootToken);

    private sealed record RekeyResponse(
        [property: JsonPropertyName("complete")] bool Complete,
        [property: JsonPropertyName("verification_required")] bool VerificationRequired,
        [property: JsonPropertyName("nonce")] string? Nonce,
        [property: JsonPropertyName("required")] int Required,
        [property: JsonPropertyName("keys_base64")] string[]? KeysBase64);
}
