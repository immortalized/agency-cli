using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Models;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class JwtAccessTokenService(
    IOptions<JwtOptions> options,
    IJwtSigningProvider signingProvider)
    : IAccessTokenService
{
    private static readonly JsonSerializerOptions
        JsonOptions = new()
        {
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly JwtOptions _options =
        options?.Value
        ?? throw new ArgumentNullException(
            nameof(options));

    private readonly IJwtSigningProvider
        _signingProvider = signingProvider
        ?? throw new ArgumentNullException(
            nameof(signingProvider));

    public async Task<AccessTokenResult> CreateAsync(
        AccessTokenSubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (subject.UserId == Guid.Empty)
        {
            throw new ArgumentException(
                "User id cannot be empty.",
                nameof(subject));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            subject.Username);

        var nowUtc = DateTimeOffset.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(
            _options.AccessTokenLifetimeMinutes);

        var header = new JwtHeader(
            "RS256",
            "JWT",
            _signingProvider.KeyId);

        var payload = new JwtPayload(
            subject.UserId.ToString(),
            subject.Username,
            Guid.NewGuid().ToString(),
            nowUtc.ToUnixTimeSeconds(),
            subject.Email,
            _options.Issuer,
            _options.Audience,
            nowUtc.ToUnixTimeSeconds(),
            expiresAtUtc.ToUnixTimeSeconds());

        var encodedHeader = Base64UrlEncode(
            JsonSerializer.SerializeToUtf8Bytes(
                header,
                JsonOptions));

        var encodedPayload = Base64UrlEncode(
            JsonSerializer.SerializeToUtf8Bytes(
                payload,
                JsonOptions));

        var signingInput = Encoding.ASCII.GetBytes(
            $"{encodedHeader}.{encodedPayload}");

        var signatureResult =
            await _signingProvider.SignAsync(
                signingInput,
                cancellationToken);

        if (signatureResult.Signature.IsEmpty)
        {
            throw new InvalidOperationException(
                "The JWT signing provider returned an empty signature.");
        }

        var encodedSignature = Base64UrlEncode(
            signatureResult.Signature.Span);

        return new AccessTokenResult(
            $"{encodedHeader}.{encodedPayload}.{encodedSignature}",
            expiresAtUtc);
    }

    private static string Base64UrlEncode(
        ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record JwtHeader(
        [property: JsonPropertyName("alg")]
        string Algorithm,

        [property: JsonPropertyName("typ")]
        string Type,

        [property: JsonPropertyName("kid")]
        string KeyId);

    private sealed record JwtPayload(
        [property: JsonPropertyName("sub")]
        string Subject,

        [property: JsonPropertyName("unique_name")]
        string Username,

        [property: JsonPropertyName("jti")]
        string JwtId,

        [property: JsonPropertyName("iat")]
        long IssuedAt,

        [property: JsonPropertyName("email")]
        string? Email,

        [property: JsonPropertyName("iss")]
        string Issuer,

        [property: JsonPropertyName("aud")]
        string Audience,

        [property: JsonPropertyName("nbf")]
        long NotBefore,

        [property: JsonPropertyName("exp")]
        long Expiration);
}
