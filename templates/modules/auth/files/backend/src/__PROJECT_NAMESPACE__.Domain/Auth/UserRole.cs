namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class UserRole
{
    private UserRole()
    {
    }

    public UserRole(
        Guid userId,
        Guid roleId,
        DateTimeOffset createdAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User id cannot be empty.",
                nameof(userId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException(
                "Role id cannot be empty.",
                nameof(roleId));
        }

        UserId = userId;
        RoleId = roleId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }

    public User User { get; private set; } = null!;

    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
