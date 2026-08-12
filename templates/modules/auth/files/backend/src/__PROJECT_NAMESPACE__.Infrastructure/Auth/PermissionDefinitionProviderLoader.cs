using __PROJECT_NAMESPACE__.Application.Auth.Authorization;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

/// <summary>
/// Builds a <see cref="PermissionCatalog"/> for hosts that have no dependency
/// injection container, such as the Operations console. The API instead
/// resolves the providers each installed module registered with the container.
/// </summary>
public static class PermissionDefinitionProviderLoader
{
    public static PermissionCatalog CreateCatalog()
    {
        var providerType =
            typeof(IPermissionDefinitionProvider);

        // Module permission providers live in either the Application or the
        // Infrastructure assembly; both are referenced directly here so they
        // are guaranteed to be loaded before this scan runs.
        var assemblies = new[]
            {
                typeof(AuthPermissionDefinitionProvider).Assembly,
                providerType.Assembly
            }
            .Distinct();

        var providers = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type is
                {
                    IsAbstract: false,
                    IsInterface: false,
                    IsGenericTypeDefinition: false
                }
                && providerType.IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type =>
                (IPermissionDefinitionProvider)Activator
                    .CreateInstance(type)!)
            .ToArray();

        return new PermissionCatalog(providers);
    }
}
