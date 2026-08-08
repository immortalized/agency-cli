namespace __PROJECT_NAMESPACE__.Api.Modules;

public interface IApplicationModule
{
    void AddServices(
        IServiceCollection services,
        IConfiguration configuration);

    void ConfigureApplication(WebApplication app);
}