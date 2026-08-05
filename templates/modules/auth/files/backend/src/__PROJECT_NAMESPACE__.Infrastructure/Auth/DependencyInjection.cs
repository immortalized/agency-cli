using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<JwtOptions>()
            .Bind(
                configuration.GetSection(
                    JwtOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<AuthOptions>,
            AuthOptionsValidator>();

        services.AddSingleton<
            IValidateOptions<JwtOptions>,
            JwtOptionsValidator>();

        services.AddSingleton<BootstrapSecretValidator>();

        services.AddSingleton<
            IPasswordHasher,
            Argon2PasswordHasher>();

        services.AddSingleton<
            IRefreshTokenService,
            RefreshTokenService>();

        services.AddSingleton<
            IAccessTokenService,
            JwtAccessTokenService>();

        return services;
    }
}