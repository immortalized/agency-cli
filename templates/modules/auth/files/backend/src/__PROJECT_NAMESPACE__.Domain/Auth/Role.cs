namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class Role
{
    private readonly List<User> _users = [];

    private Role()
    {
    }

    public Role(
        Guid id,
        string name,
        string normalizedName,
        string displayName,
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
        IsSystem = isSystem;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string NormalizedName { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<User> Users
        => _users.AsReadOnly();
}