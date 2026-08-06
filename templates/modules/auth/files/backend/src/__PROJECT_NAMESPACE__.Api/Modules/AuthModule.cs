using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace __PROJECT_NAMESPACE__.Api.Modules;

public sealed class AuthModule
    : IApplicationModule,
      IDisposable
{
    private RSA? _publicKey;
    private bool _disposed;

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(
            services);

        ArgumentNullException.ThrowIfNull(
            configuration);

        services.AddAuthInfrastructure(
            configuration);

        var jwtOptions = configuration
            .GetRequiredSection(
                JwtOptions.SectionName)
            .Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{JwtOptions.SectionName}' is missing.");

        _publicKey =
            RsaKeyLoader.LoadPublicKeyFromFile(
                jwtOptions.PublicKeyFile);

        var validationKey =
            new RsaSecurityKey(_publicKey)
            {
                KeyId = jwtOptions.KeyId
            };

        services
            .AddAuthentication(
                JwtBearerDefaults
                    .AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;

                options.RequireHttpsMetadata = true;

                options.SaveToken = false;

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,

                        ValidIssuer =
                            jwtOptions.Issuer,

                        ValidateAudience = true,

                        ValidAudience =
                            jwtOptions.Audience,

                        ValidateIssuerSigningKey =
                            true,

                        IssuerSigningKey =
                            validationKey,

                        RequireSignedTokens = true,

                        RequireExpirationTime = true,

                        ValidateLifetime = true,

                        ClockSkew =
                            TimeSpan.FromSeconds(30),

                        NameClaimType =
                            JwtRegisteredClaimNames
                                .UniqueName,

                        AuthenticationType =
                            JwtBearerDefaults
                                .AuthenticationScheme
                    };
            });

        services.AddAuthorization();
    }

    public void ConfigureApplication(
        WebApplication app)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseAuthorization();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _publicKey?.Dispose();
        _publicKey = null;

        _disposed = true;

        GC.SuppressFinalize(this);
    }
}