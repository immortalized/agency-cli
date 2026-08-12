using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

/// <summary>
/// Applies the shared credential-invalidation pattern: bump the user's auth
/// version so already-issued access tokens fail the per-request security-state
/// check, and revoke the refresh tokens that could mint replacements.
/// </summary>
/// <remarks>
/// Every method mutates tracked entities only. The caller owns the unit of
/// work and must still call <c>SaveChangesAsync</c>.
/// </remarks>
public sealed class UserSessionInvalidator(AppDbContext dbContext)
{
    /// <summary>
    /// Revokes active refresh tokens without touching the auth version. Use
    /// this when the change already incremented it on the user entity.
    /// </summary>
    public async Task RevokeRefreshTokensAsync(
        IReadOnlyCollection<Guid> userIds,
        string reason,
        string? ipAddress,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (userIds.Count == 0)
        {
            return;
        }

        var tokens = await dbContext.Set<RefreshToken>()
            .Where(token => userIds.Contains(token.UserId))
            .ToListAsync(cancellationToken);

        RevokeActive(tokens, reason, ipAddress, nowUtc);
    }

    public async Task RevokeFamilyAsync(
        Guid familyId,
        string reason,
        string? ipAddress,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var tokens = await dbContext.Set<RefreshToken>()
            .Where(token => token.FamilyId == familyId)
            .ToListAsync(cancellationToken);

        RevokeActive(tokens, reason, ipAddress, nowUtc);
    }

    /// <summary>
    /// Increments the auth version of each user and revokes their refresh
    /// tokens. Use this when the authorization change happened somewhere other
    /// than the user row, such as a role's permission set being edited.
    /// </summary>
    public async Task InvalidateAsync(
        IReadOnlyCollection<Guid> userIds,
        string reason,
        string? ipAddress,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return;
        }

        var users = await dbContext.Set<User>()
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            user.InvalidateSessions(nowUtc);
        }

        await RevokeRefreshTokensAsync(
            userIds,
            reason,
            ipAddress,
            nowUtc,
            cancellationToken);
    }

    /// <summary>
    /// Returns the ids of every user currently assigned the given role.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetRoleMemberIdsAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<UserRole>()
            .Where(assignment => assignment.RoleId == roleId)
            .Select(assignment => assignment.UserId)
            .ToListAsync(cancellationToken);

    private static void RevokeActive(
        IReadOnlyCollection<RefreshToken> tokens,
        string reason,
        string? ipAddress,
        DateTimeOffset nowUtc)
    {
        foreach (var token in tokens.Where(
                     token => token.IsActive(nowUtc)))
        {
            token.Revoke(nowUtc, ipAddress, reason);
        }
    }
}
