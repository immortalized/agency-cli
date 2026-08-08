using __PROJECT_NAMESPACE__.Infrastructure;

namespace __PROJECT_NAMESPACE__.Api.Modules;

public sealed class DatabaseModule
    : IApplicationModule
{
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDatabaseInfrastructure(
            configuration,
            AppContext.BaseDirectory);
    }

    public void ConfigureApplication(
        WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
    }
}
