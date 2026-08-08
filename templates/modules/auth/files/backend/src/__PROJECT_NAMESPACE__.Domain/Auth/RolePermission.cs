namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public RolePermission(
        Guid roleId,
        Guid permissionId,
        DateTimeOffset createdAtUtc)
    {
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException(
                "Role id cannot be empty.",
                nameof(roleId));
        }

        if (permissionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Permission id cannot be empty.",
                nameof(permissionId));
        }

        RoleId = roleId;
        PermissionId = permissionId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = null!;

    public Guid PermissionId { get; private set; }

    public Permission Permission { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
