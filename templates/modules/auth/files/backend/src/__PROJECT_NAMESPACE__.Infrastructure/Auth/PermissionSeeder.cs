using System.Data;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class PermissionSeeder(
    AppDbContext dbContext,
    PermissionCatalog catalog)
{
    public const string AdministratorRoleName = "administrator";
    public const string UserRoleName = "user";

    private const string AdministratorDescription =
        "Built-in role that always holds every installed permission.";

    private const string UserDescription =
        "Built-in default role assigned to newly created accounts.";

    /// <summary>
    /// The built-in role whose permission set this seeder owns. Editing it
    /// through the admin API is refused because the next startup would
    /// re-grant every installed permission anyway.
    /// </summary>
    public static bool IsPermissionSetManaged(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return role.IsSystem
            && string.Equals(
                role.NormalizedName,
                AuthNormalizer.NormalizeUsername(
                    AdministratorRoleName),
                StringComparison.Ordinal);
    }

    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = catalog.Definitions;

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(6843229417055901772)",
            cancellationToken);

        var nowUtc = DateTimeOffset.UtcNow;

        var administrator = await EnsureRoleAsync(
            AdministratorRoleName,
            "Administrator",
            AdministratorDescription,
            nowUtc,
            cancellationToken);

        await EnsureRoleAsync(
            UserRoleName,
            "User",
            UserDescription,
            nowUtc,
            cancellationToken);

        var existingPermissions = await dbContext
            .Set<Permission>()
            .ToDictionaryAsync(
                permission => permission.Name,
                StringComparer.Ordinal,
                cancellationToken);

        foreach (var definition in definitions)
        {
            if (existingPermissions.TryGetValue(
                    definition.Name,
                    out var existing))
            {
                if (!string.Equals(
                        existing.Module,
                        definition.Module,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Permission '{definition.Name}' is already owned by module '{existing.Module}'.");
                }

                continue;
            }

            var permission = new Permission(
                PermissionId.FromName(definition.Name),
                definition.Name,
                definition.Module,
                definition.Description,
                nowUtc);

            dbContext.Set<Permission>().Add(permission);
            existingPermissions.Add(permission.Name, permission);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var assignedPermissionIds = await dbContext
            .Set<RolePermission>()
            .Where(mapping => mapping.RoleId == administrator.Id)
            .Select(mapping => mapping.PermissionId)
            .ToHashSetAsync(cancellationToken);

        var grantsAdded = 0;

        foreach (var definition in definitions)
        {
            var permission = existingPermissions[definition.Name];

            if (assignedPermissionIds.Add(permission.Id))
            {
                dbContext.Set<RolePermission>().Add(
                    new RolePermission(
                        administrator.Id,
                        permission.Id,
                        nowUtc));

                grantsAdded++;
            }
        }

        if (grantsAdded > 0)
        {
            administrator.Touch(nowUtc);

            // Installing a module widens the administrator permission set, so
            // access tokens minted before this startup no longer describe it.
            // Only the auth version is bumped here: refresh tokens stay valid
            // so members transparently pick up the wider set on their next
            // refresh instead of being logged out by every module install.
            await InvalidateRoleMembersAsync(
                administrator.Id,
                nowUtc,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task InvalidateRoleMembersAsync(
        Guid roleId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var memberIds = await dbContext.Set<UserRole>()
            .Where(assignment => assignment.RoleId == roleId)
            .Select(assignment => assignment.UserId)
            .ToListAsync(cancellationToken);

        if (memberIds.Count == 0)
        {
            return;
        }

        var members = await dbContext.Set<User>()
            .Where(user => memberIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        foreach (var member in members)
        {
            member.InvalidateSessions(nowUtc);
        }
    }

    private async Task<Role> EnsureRoleAsync(
        string name,
        string displayName,
        string description,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var normalizedName = AuthNormalizer.NormalizeUsername(name);

        var role = await dbContext.Set<Role>()
            .SingleOrDefaultAsync(
                candidate => candidate.NormalizedName == normalizedName,
                cancellationToken);

        if (role is null)
        {
            role = new Role(
                PermissionId.FromName($"role:{name}"),
                name,
                normalizedName,
                displayName,
                description,
                true,
                nowUtc);

            dbContext.Set<Role>().Add(role);
            return role;
        }

        if (!role.IsSystem || !role.IsActive)
        {
            throw new InvalidOperationException(
                $"The existing '{name}' role is not a valid active system role.");
        }

        return role;
    }
}
