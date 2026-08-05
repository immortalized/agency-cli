using __PROJECT_NAMESPACE__.Api.Modules;
using __PROJECT_NAMESPACE__.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var applicationModules = ApplicationModuleLoader.Discover();

foreach (var module in applicationModules)
{
    module.AddServices(
        builder.Services,
        builder.Configuration);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

foreach (var module in applicationModules)
{
    module.ConfigureApplication(app);
}

//app.UseHttpsRedirection();

app.MapControllers();

app.Run();