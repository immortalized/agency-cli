using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Application.Menu;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace __PROJECT_NAMESPACE__.Api.Modules;

public sealed class MenuModule : IApplicationModule
{
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPermissionDefinitionProvider,
                MenuPermissionDefinitionProvider>());
    }

    public void ConfigureApplication(WebApplication app)
    {
    }
}
