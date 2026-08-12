namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class User
{
    private readonly List<RefreshToken> _refreshTokens = [];
    private readonly List<UserRole> _userRoles = [];

    private User()
    {
    }

    public User(
        Guid id,
        IReadOnlyCollection<Guid> roleIds,
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

        ArgumentNullException.ThrowIfNull(roleIds);

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

        foreach (var roleId in roleIds.Distinct())
        {
            if (roleId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Role id cannot be empty.",
                    nameof(roleIds));
            }

            _userRoles.Add(
                new UserRole(id, roleId, createdAtUtc));
        }
    }

    public Guid Id { get; private set; }

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

    public IReadOnlyCollection<UserRole> UserRoles
        => _userRoles.AsReadOnly();

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

    public void RehashPassword(
        string passwordHash,
        DateTimeOffset changedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
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

    public void UpdateProfile(
        string username,
        string normalizedUsername,
        string? email,
        string? normalizedEmail,
        DateTimeOffset changedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);

        Username = username;
        NormalizedUsername = normalizedUsername;
        ChangeEmail(email, normalizedEmail, changedAtUtc);
        AuthVersion++;
        UpdatedAtUtc = changedAtUtc;
    }

    /// <summary>
    /// Replaces the user's role assignments. Returns <c>true</c> when the
    /// assignment actually changed, in which case the auth version was
    /// incremented and every previously issued access token is now stale.
    /// The <see cref="UserRoles"/> navigation must be loaded before calling.
    /// </summary>
    public bool ReplaceRoles(
        IReadOnlyCollection<Guid> roleIds,
        DateTimeOffset changedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        var desired = new HashSet<Guid>(roleIds);

        if (desired.Contains(Guid.Empty))
        {
            throw new ArgumentException(
                "Role id cannot be empty.",
                nameof(roleIds));
        }

        var current = _userRoles
            .Select(assignment => assignment.RoleId)
            .ToHashSet();

        if (desired.SetEquals(current))
        {
            return false;
        }

        _userRoles.RemoveAll(
            assignment => !desired.Contains(assignment.RoleId));

        foreach (var roleId in desired.Where(
                     roleId => !current.Contains(roleId)))
        {
            _userRoles.Add(
                new UserRole(Id, roleId, changedAtUtc));
        }

        AuthVersion++;
        UpdatedAtUtc = changedAtUtc;
        return true;
    }

    /// <summary>
    /// Increments the auth version without any other state change, so access
    /// tokens issued before an out-of-band authorization change (such as a
    /// role's permission set being edited) stop validating immediately.
    /// </summary>
    public void InvalidateSessions(DateTimeOffset changedAtUtc)
    {
        AuthVersion++;
        UpdatedAtUtc = changedAtUtc;
    }

    public void SetTemporaryPassword(
        string passwordHash,
        DateTimeOffset changedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        MustChangePassword = true;
        PasswordChangedAtUtc = changedAtUtc;
        AuthVersion++;
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
