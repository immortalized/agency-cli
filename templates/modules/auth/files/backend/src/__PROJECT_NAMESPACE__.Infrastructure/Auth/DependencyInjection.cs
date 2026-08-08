using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Infrastructure.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public static class DependencyInjection
{
    public static IServiceCollection
        AddAuthInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(
            services);

        ArgumentNullException.ThrowIfNull(
            configuration);

        services
            .AddOptions<AuthOptions>()
            .Bind(
                configuration.GetSection(
                    AuthOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<JwtOptions>()
            .Bind(
                configuration.GetSection(
                    JwtOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<
                OpenBaoDatabaseCredentialOptions>()
            .Bind(
                configuration.GetSection(
                    OpenBaoDatabaseCredentialOptions
                        .SectionName))
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<AuthOptions>,
            AuthOptionsValidator>();

        services.AddSingleton<
            IValidateOptions<JwtOptions>,
            JwtOptionsValidator>();

        services.AddSingleton<
            IValidateOptions<
                OpenBaoDatabaseCredentialOptions>,
            OpenBaoDatabaseCredentialOptionsValidator>();

        services.AddSingleton<
            IPasswordHasher,
            Argon2PasswordHasher>();

        services.AddSingleton<
            ITemporaryPasswordGenerator,
            TemporaryPasswordGenerator>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPermissionDefinitionProvider,
                AuthPermissionDefinitionProvider>());

        services.AddScoped<PermissionSeeder>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IDatabaseStartupTask,
                PermissionSeedStartupTask>());

        services.AddSingleton<
            IRefreshTokenService,
            RefreshTokenService>();

        services.AddHttpClient<
                IJwtSigningProvider,
                OpenBaoJwtSigningProvider>(
                (serviceProvider, httpClient) =>
                {
                    var jwtOptions =
                        serviceProvider
                            .GetRequiredService<
                                IOptions<JwtOptions>>()
                            .Value;

                    httpClient.BaseAddress = new Uri(
                        jwtOptions.OpenBao.Address,
                        UriKind.Absolute);

                    httpClient.Timeout =
                        TimeSpan.FromSeconds(
                            jwtOptions.OpenBao
                                .RequestTimeoutSeconds);
                });

        services.AddTransient<
            IAccessTokenService,
            JwtAccessTokenService>();

        services.AddHttpClient<
                IDatabaseCredentialProvider,
                OpenBaoDatabaseCredentialProvider>(
                (serviceProvider, httpClient) =>
                {
                    var options = serviceProvider
                        .GetRequiredService<
                            IOptions<
                                OpenBaoDatabaseCredentialOptions>>()
                        .Value;

                    httpClient.BaseAddress = new Uri(
                        options.Address,
                        UriKind.Absolute);

                    httpClient.Timeout =
                        TimeSpan.FromSeconds(
                            options
                                .RequestTimeoutSeconds);
                });

        return services;
    }
}
