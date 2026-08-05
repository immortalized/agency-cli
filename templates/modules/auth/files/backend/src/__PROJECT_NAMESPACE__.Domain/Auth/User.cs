namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class User
{
    private readonly List<RefreshToken> _refreshTokens = [];

    private User()
    {
    }

    public User(
        Guid id,
        string username,
        string normalizedUsername,
        string passwordHash,
        DateTimeOffset createdAtUtc,
        string? email = null,
        string? normalizedEmail = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "User id cannot be empty.",
                nameof(id));
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
        Username = username;
        NormalizedUsername = normalizedUsername;
        Email = email;
        NormalizedEmail = normalizedEmail;
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Username { get; private set; } = null!;

    public string NormalizedUsername { get; private set; } = null!;

    public string? Email { get; private set; }

    public string? NormalizedEmail { get; private set; }

    public string PasswordHash { get; private set; } = null!;

    public bool IsActive { get; private set; }

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
        PasswordChangedAtUtc = changedAtUtc;
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

    public void RecordSuccessfulLogin(DateTimeOffset loggedInAtUtc)
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
        UpdatedAtUtc = enabledAtUtc;
    }
}