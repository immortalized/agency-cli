namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class User
{
    private readonly List<RefreshToken> _refreshTokens = [];

    private User()
    {
    }

    public User(
        Guid id,
        Guid roleId,
        string username,
        string normalizedUsername,
        string passwordHash,
        DateTimeOffset createdAtUtc,
        bool mustChangePassword,
        string? email = null,
        string? normalizedEmail = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "User id cannot be empty.",
                nameof(id));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException(
                "Role id cannot be empty.",
                nameof(roleId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (email is null && normalizedEmail is not null)
        {
            throw new ArgumentException(
                "Normalized email must be null when email is null.",
                nameof(normalizedEmail));
        }

        if (email is not null &&
            string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException(
                "Normalized email is required when email is provided.",
                nameof(normalizedEmail));
        }

        Id = id;
        RoleId = roleId;
        Username = username;
        NormalizedUsername = normalizedUsername;
        Email = email;
        NormalizedEmail = normalizedEmail;
        PasswordHash = passwordHash;
        IsActive = true;
        MustChangePassword = mustChangePassword;
        AuthVersion = 1;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = null!;

    public string Username { get; private set; } = null!;

    public string NormalizedUsername { get; private set; } = null!;

    public string? Email { get; private set; }

    public string? NormalizedEmail { get; private set; }

    public string PasswordHash { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public bool MustChangePassword { get; private set; }

    public long AuthVersion { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public DateTimeOffset? PasswordChangedAtUtc { get; private set; }

    public DateTimeOffset? DisabledAtUtc { get; private set; }

    public IReadOnlyCollection<RefreshToken> RefreshTokens
        => _refreshTokens.AsReadOnly();

    public void ChangePassword(
        string passwordHash,
        DateTimeOffset changedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        MustChangePassword = false;
        PasswordChangedAtUtc = changedAtUtc;
        AuthVersion++;
        UpdatedAtUtc = changedAtUtc;
    }

    public void ChangeEmail(
        string? email,
        string? normalizedEmail,
        DateTimeOffset changedAtUtc)
    {
        if (email is null && normalizedEmail is not null)
        {
            throw new ArgumentException(
                "Normalized email must be null when email is null.",
                nameof(normalizedEmail));
        }

        if (email is not null &&
            string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException(
                "Normalized email is required when email is provided.",
                nameof(normalizedEmail));
        }

        Email = email;
        NormalizedEmail = normalizedEmail;
        UpdatedAtUtc = changedAtUtc;
    }

    public void RecordSuccessfulLogin(
        DateTimeOffset loggedInAtUtc)
    {
        LastLoginAtUtc = loggedInAtUtc;
    }

    public void Disable(DateTimeOffset disabledAtUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DisabledAtUtc = disabledAtUtc;
        AuthVersion++;
        UpdatedAtUtc = disabledAtUtc;
    }

    public void Enable(DateTimeOffset enabledAtUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        DisabledAtUtc = null;
        AuthVersion++;
        UpdatedAtUtc = enabledAtUtc;
    }
}