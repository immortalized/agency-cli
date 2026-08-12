namespace __PROJECT_NAMESPACE__.Application.Auth.Authorization;

public static class AuthPermissions
{
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDisable = "users.disable";
    public const string UsersAssignRoles = "users.assign-roles";

    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string RolesDelete = "roles.delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(UsersRead, "auth", "Read user accounts."),
        new(UsersCreate, "auth", "Create user accounts."),
        new(UsersUpdate, "auth", "Update user account profiles."),
        new(UsersDisable, "auth", "Disable or enable user accounts."),
        new(UsersAssignRoles, "auth", "Assign roles to user accounts."),
        new(RolesRead, "auth", "Read roles and the permission catalog."),
        new(RolesCreate, "auth", "Create custom roles."),
        new(RolesUpdate, "auth", "Rename roles and change their permission sets."),
        new(RolesDelete, "auth", "Delete custom roles.")
    ];
}
