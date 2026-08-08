using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Threading.RateLimiting;
using __PROJECT_NAMESPACE__.Api.Authorization;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace __PROJECT_NAMESPACE__.Api.Modules;

public sealed class AuthModule
    : IApplicationModule,
      IDisposable
{
    private JwtKeyRing? _keyRing;
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

        _keyRing = JwtKeyRing.Load(
            jwtOptions.KeyRingFile);

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

                        IssuerSigningKeys =
                            _keyRing.ValidationKeys,

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
                                .AuthenticationScheme,

                        RoleClaimType =
                            AuthClaimNames.Role
                    };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated =
                        ValidateSecurityStateAsync
                };
            });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy =
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .RequireClaim(
                        AuthClaimNames.MustChangePassword,
                        bool.FalseString.ToLowerInvariant())
                    .Build();

            options.AddPolicy(
                AuthPolicies.PasswordChangeAllowed,
                policy => policy
                    .RequireAuthenticatedUser());
        });

        services.AddSingleton<IAuthorizationHandler,
            PermissionAuthorizationHandler>();

        services.AddSingleton<IAuthorizationPolicyProvider,
            PermissionAuthorizationPolicyProvider>();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                AuthRateLimitPolicy.Name,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?
                            .ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));

            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;
        });
    }

    private static async Task ValidateSecurityStateAsync(
        TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        var authVersion = context.Principal?.FindFirst(
            AuthClaimNames.AuthVersion)?.Value;

        var mustChangePassword = context.Principal?.FindFirst(
            AuthClaimNames.MustChangePassword)?.Value;

        if (!Guid.TryParse(subject, out var userId)
            || !long.TryParse(
                authVersion,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var tokenAuthVersion)
            || !bool.TryParse(
                mustChangePassword,
                out var tokenMustChangePassword))
        {
            context.Fail("The access token security state is invalid.");
            return;
        }

        var dbContext = context.HttpContext
            .RequestServices
            .GetRequiredService<AppDbContext>();

        var userState = await dbContext.Set<User>()
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.IsActive,
                user.AuthVersion,
                user.MustChangePassword
            })
            .SingleOrDefaultAsync(
                context.HttpContext.RequestAborted);

        if (userState is null
            || !userState.IsActive
            || userState.AuthVersion != tokenAuthVersion
            || userState.MustChangePassword
                != tokenMustChangePassword)
        {
            context.Fail("The access token security state is stale.");
        }
    }

    public void ConfigureApplication(
        WebApplication app)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(app);

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapGet(
                    "/api/auth/dev/validate-token",
                    () => Results.NoContent())
                .RequireAuthorization();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keyRing?.Dispose();
        _keyRing = null;

        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
