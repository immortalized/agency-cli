namespace __PROJECT_NAMESPACE__.Api.Contracts.Roles;

public sealed record PermissionResponse(
    string Name,
    string Module,
    string Description);
