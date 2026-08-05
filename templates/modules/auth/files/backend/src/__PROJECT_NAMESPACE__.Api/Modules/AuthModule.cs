using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace __PROJECT_NAMESPACE__.Api.Modules;

public sealed class AuthModule : IApplicationModule
{
    private RSA? _publicKey;

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthInfrastructure(configuration);

        var jwtOptions = configuration
            .GetRequiredSection(JwtOptions.SectionName)
            .Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{JwtOptions.SectionName}' is missing.");

        _publicKey = RsaKeyLoader.LoadPublicKey(
            jwtOptions.PublicKeyPem);

        var validationKey =
            new RsaSecurityKey(_publicKey)
            {
                KeyId = jwtOptions.KeyId
            };

        services
            .AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;

                options.RequireHttpsMetadata = true;

                options.SaveToken = false;

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtOptions.Issuer,

                        ValidateAudience = true,
                        ValidAudience = jwtOptions.Audience,

                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = validationKey,

                        RequireSignedTokens = true,
                        RequireExpirationTime = true,
                        ValidateLifetime = true,

                        ClockSkew = TimeSpan.FromSeconds(30),

                        NameClaimType =
                            JwtRegisteredClaimNames.UniqueName,

                        AuthenticationType =
                            JwtBearerDefaults.AuthenticationScheme
                    };
            });

        services.AddAuthorization();
    }

    public void ConfigureApplication(
        WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseAuthorization();
    }
}