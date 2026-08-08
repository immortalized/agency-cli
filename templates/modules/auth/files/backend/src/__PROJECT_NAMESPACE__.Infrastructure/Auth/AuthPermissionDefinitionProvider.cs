using __PROJECT_NAMESPACE__.Application.Auth.Authorization;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class AuthPermissionDefinitionProvider
    : IPermissionDefinitionProvider
{
    public IReadOnlyCollection<PermissionDefinition>
        GetPermissions() => AuthPermissions.All;
}
