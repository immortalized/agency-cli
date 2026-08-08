namespace __PROJECT_NAMESPACE__.Application.Auth.Authorization;

public sealed record PermissionDefinition(
    string Name,
    string Module,
    string Description);
