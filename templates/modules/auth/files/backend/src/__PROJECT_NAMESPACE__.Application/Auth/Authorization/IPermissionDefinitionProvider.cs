namespace __PROJECT_NAMESPACE__.Application.Auth.Authorization;

public interface IPermissionDefinitionProvider
{
    IReadOnlyCollection<PermissionDefinition> GetPermissions();
}
