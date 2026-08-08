namespace __PROJECT_NAMESPACE__.Application.Auth.Authorization;

public static class AuthPolicies
{
    public const string PasswordChangeAllowed =
        "Auth.PasswordChangeAllowed";

    public const string PermissionPrefix =
        "Permission:";

    public static string ForPermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return $"{PermissionPrefix}{permission}";
    }
}
