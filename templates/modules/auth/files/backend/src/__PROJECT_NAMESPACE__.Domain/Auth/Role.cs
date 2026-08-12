namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class Role
{
    private readonly List<UserRole> _userRoles = [];
    private readonly List<RolePermission> _rolePermissions = [];

    private Role()
    {
    }

    public Role(
        Guid id,
        string name,
        string normalizedName,
        string displayName,
        string? description,
        bool isSystem,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Role id cannot be empty.",
                nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        Name = name;
        NormalizedName = normalizedName;
        DisplayName = displayName;
        Description = NormalizeDescription(description);
        IsSystem = isSystem;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string NormalizedName { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<UserRole> UserRoles
        => _userRoles.AsReadOnly();

    public IReadOnlyCollection<RolePermission> RolePermissions
        => _rolePermissions.AsReadOnly();

    public void UpdateDetails(
        string name,
        string normalizedName,
        string displayName,
        string? description,
        DateTimeOffset changedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        // Built-in role names are looked up by the seeder and by the
        // registration flow, so only their presentation may change.
        if (IsSystem
            && !string.Equals(
                NormalizedName,
                normalizedName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Built-in roles cannot be renamed.");
        }

        Name = name;
        NormalizedName = normalizedName;
        DisplayName = displayName;
        Description = NormalizeDescription(description);
        UpdatedAtUtc = changedAtUtc;
    }

    public void Touch(DateTimeOffset changedAtUtc)
    {
        UpdatedAtUtc = changedAtUtc;
    }

    private static string? NormalizeDescription(
        string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
}
