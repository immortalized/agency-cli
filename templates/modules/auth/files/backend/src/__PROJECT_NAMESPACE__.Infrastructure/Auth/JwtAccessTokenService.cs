using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class JwtAccessTokenService
    : IAccessTokenService,
      IDisposable
{
    private readonly JwtOptions _options;
    private readonly RSA _privateKey;
    private readonly SigningCredentials
        _signingCredentials;

    private readonly JwtSecurityTokenHandler
        _tokenHandler = new();

    private bool _disposed;

    public JwtAccessTokenService(
        IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        _privateKey =
            RsaKeyLoader.LoadPrivateKeyFromFile(
                _options.PrivateKeyFile);

        var securityKey =
            new RsaSecurityKey(_privateKey)
            {
                KeyId = _options.KeyId
            };

        _signingCredentials =
            new SigningCredentials(
                securityKey,
                SecurityAlgorithms.RsaSha256);
    }

    public AccessTokenResult Create(
        AccessTokenSubject subject)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

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

        var claims = new List<Claim>
        {
            new(
                JwtRegisteredClaimNames.Sub,
                subject.UserId.ToString()),

            new(
                JwtRegisteredClaimNames.UniqueName,
                subject.Username),

            new(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString()),

            new(
                JwtRegisteredClaimNames.Iat,
                nowUtc
                    .ToUnixTimeSeconds()
                    .ToString(),
                ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrWhiteSpace(
                subject.Email))
        {
            claims.Add(
                new Claim(
                    JwtRegisteredClaimNames.Email,
                    subject.Email));
        }

        var descriptor =
            new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                Subject =
                    new ClaimsIdentity(claims),
                NotBefore =
                    nowUtc.UtcDateTime,
                IssuedAt =
                    nowUtc.UtcDateTime,
                Expires =
                    expiresAtUtc.UtcDateTime,
                SigningCredentials =
                    _signingCredentials
            };

        var securityToken =
            _tokenHandler.CreateToken(
                descriptor);

        var token =
            _tokenHandler.WriteToken(
                securityToken);

        return new AccessTokenResult(
            token,
            expiresAtUtc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _privateKey.Dispose();

        _disposed = true;

        GC.SuppressFinalize(this);
    }
}