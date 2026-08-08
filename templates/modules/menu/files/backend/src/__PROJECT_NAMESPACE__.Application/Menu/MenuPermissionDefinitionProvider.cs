using __PROJECT_NAMESPACE__.Application.Auth.Authorization;

namespace __PROJECT_NAMESPACE__.Application.Menu;

public sealed class MenuPermissionDefinitionProvider
    : IPermissionDefinitionProvider
{
    public IReadOnlyCollection<PermissionDefinition>
        GetPermissions() => MenuPermissions.All;
}
