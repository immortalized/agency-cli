using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Models;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class OpenBaoJwtSigningProvider
    : IJwtSigningProvider,
      IDisposable
{
    private const int Rsa3072SignatureLengthBytes =
        384;

    private readonly HttpClient _httpClient;
    private readonly JwtKeyRing _keyRing;
    private readonly string _signPath;
    private bool _disposed;

    public OpenBaoJwtSigningProvider(
        HttpClient httpClient,
        IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;

        var jwtOptions = options.Value;
        _keyRing = JwtKeyRing.Load(
            jwtOptions.KeyRingFile);

        KeyId = _keyRing.ActiveKeyId;

        var openBao = jwtOptions.OpenBao;
        _signPath = string.Join(
            '/',
            "v1",
            Uri.EscapeDataString(openBao.TransitMount),
            "sign",
            Uri.EscapeDataString(openBao.KeyName),
            "sha2-256");

        var token = ReadToken(openBao.TokenFile);

        _httpClient.DefaultRequestHeaders.Add(
            "X-Vault-Token",
            token);
    }

    public string KeyId { get; }

    public async Task<JwtSignatureResult> SignAsync(
        ReadOnlyMemory<byte> signingInput,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (signingInput.IsEmpty)
        {
            throw new ArgumentException(
                "JWT signing input cannot be empty.",
                nameof(signingInput));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _signPath)
        {
            Content = JsonContent.Create(
                new SignRequest(
                    Convert.ToBase64String(
                        signingInput.Span),
                    _keyRing.ActiveKeyVersion,
                    false,
                    "pkcs1v15"))
        };

        using var response = await SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"OpenBao Transit signing failed with HTTP status {(int)response.StatusCode}.");
        }

        SignResponse? result;

        try
        {
            result = await response.Content
                .ReadFromJsonAsync<SignResponse>(
                    cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned malformed JSON.",
                exception);
        }

        var signature = ParseSignature(
            result?.Data?.Signature,
            _keyRing.ActiveKeyVersion);

        return new JwtSignatureResult(signature);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keyRing.Dispose();
        _disposed = true;
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
                "OpenBao Transit signing timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "OpenBao Transit signing could not be completed.",
                exception);
        }
    }

    private static byte[] ParseSignature(
        string? formattedSignature,
        int expectedKeyVersion)
    {
        if (string.IsNullOrWhiteSpace(
                formattedSignature))
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned no signature.");
        }

        var parts = formattedSignature.Split(':');

        if (parts is not ["vault", var version, var encoded]
            || !version.StartsWith('v')
            || !int.TryParse(
                version.AsSpan(1),
                out var keyVersion)
            || keyVersion != expectedKeyVersion)
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned an unexpected signature version.");
        }

        byte[] signature;

        try
        {
            signature = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned an invalid signature encoding.",
                exception);
        }

        if (signature.Length !=
            Rsa3072SignatureLengthBytes)
        {
            throw new InvalidOperationException(
                "OpenBao Transit returned an invalid RSA-3072 signature length.");
        }

        return signature;
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

    private sealed record SignRequest(
        [property: JsonPropertyName("input")]
        string Input,

        [property: JsonPropertyName("key_version")]
        int KeyVersion,

        [property: JsonPropertyName("prehashed")]
        bool Prehashed,

        [property: JsonPropertyName("signature_algorithm")]
        string SignatureAlgorithm);

    private sealed record SignResponse(
        [property: JsonPropertyName("data")]
        SignResponseData? Data);

    private sealed record SignResponseData(
        [property: JsonPropertyName("signature")]
        string? Signature);
}
