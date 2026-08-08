namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class Permission
{
    private readonly List<RolePermission> _rolePermissions = [];

    private Permission()
    {
    }

    public Permission(
        Guid id,
        string name,
        string module,
        string description,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Permission id cannot be empty.",
                nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Id = id;
        Name = name;
        Module = module;
        Description = description;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Module { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<RolePermission> RolePermissions
        => _rolePermissions.AsReadOnly();
}
