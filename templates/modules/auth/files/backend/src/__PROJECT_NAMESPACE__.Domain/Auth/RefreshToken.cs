namespace __PROJECT_NAMESPACE__.Domain.Auth;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        string? createdByIpAddress,
        string? userAgent)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Refresh token id cannot be empty.",
                nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User id cannot be empty.",
                nameof(userId));
        }

        if (familyId == Guid.Empty)
        {
            throw new ArgumentException(
                "Refresh token family id cannot be empty.",
                nameof(familyId));
        }

        ArgumentNullException.ThrowIfNull(tokenHash);

        if (tokenHash.Length != 32)
        {
            throw new ArgumentException(
                "Refresh token hash must be exactly 32 bytes.",
                nameof(tokenHash));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException(
                "Expiry must be after creation.",
                nameof(expiresAtUtc));
        }

        Id = id;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash.ToArray();
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByIpAddress = createdByIpAddress;
        UserAgent = userAgent;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public User User { get; private set; } = null!;

    public Guid FamilyId { get; private set; }

    public byte[] TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string? CreatedByIpAddress { get; private set; }

    public string? RevokedByIpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? RevocationReason { get; private set; }

    public bool IsExpired(DateTimeOffset nowUtc)
        => nowUtc >= ExpiresAtUtc;

    public bool IsActive(DateTimeOffset nowUtc)
        => UsedAtUtc is null
           && RevokedAtUtc is null
           && !IsExpired(nowUtc);

    public void MarkAsUsed(
        Guid replacementTokenId,
        DateTimeOffset usedAtUtc)
    {
        if (replacementTokenId == Guid.Empty)
        {
            throw new ArgumentException(
                "Replacement token id cannot be empty.",
                nameof(replacementTokenId));
        }

        if (!IsActive(usedAtUtc))
        {
            throw new InvalidOperationException(
                "Only active refresh tokens can be rotated.");
        }

        UsedAtUtc = usedAtUtc;
        ReplacedByTokenId = replacementTokenId;
    }

    public void Revoke(
        DateTimeOffset revokedAtUtc,
        string? revokedByIpAddress,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        RevokedByIpAddress = revokedByIpAddress;
        RevocationReason = reason;
    }
}