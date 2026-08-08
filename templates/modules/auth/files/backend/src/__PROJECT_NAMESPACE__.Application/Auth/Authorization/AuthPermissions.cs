namespace __PROJECT_NAMESPACE__.Application.Auth.Authorization;

public static class AuthPermissions
{
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDisable = "users.disable";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(UsersRead, "auth", "Read user accounts."),
        new(UsersCreate, "auth", "Create user accounts."),
        new(UsersUpdate, "auth", "Update user accounts and roles."),
        new(UsersDisable, "auth", "Disable or enable user accounts.")
    ];
}
