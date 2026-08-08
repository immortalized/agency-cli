using System.Reflection;

namespace __PROJECT_NAMESPACE__.Api.Modules;

public static class ApplicationModuleLoader
{
    public static IReadOnlyList<IApplicationModule> Discover()
    {
        var moduleType = typeof(IApplicationModule);

        return Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(type =>
                type is
                {
                    IsAbstract: false,
                    IsInterface: false
                }
                && moduleType.IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(CreateModule)
            .ToArray();
    }

    private static IApplicationModule CreateModule(Type moduleType)
    {
        try
        {
            return (IApplicationModule)(
                Activator.CreateInstance(moduleType)
                ?? throw new InvalidOperationException(
                    $"Unable to create application module '{moduleType.FullName}'."));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Application module '{moduleType.FullName}' could not be initialized.",
                exception);
        }
    }
}