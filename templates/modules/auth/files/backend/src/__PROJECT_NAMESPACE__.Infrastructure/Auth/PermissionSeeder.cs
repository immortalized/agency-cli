using System.Data;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class PermissionSeeder(
    AppDbContext dbContext,
    IEnumerable<IPermissionDefinitionProvider> providers)
{
    public const string AdministratorRoleName = "administrator";
    public const string UserRoleName = "user";

    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = providers
            .SelectMany(provider => provider.GetPermissions())
            .OrderBy(definition => definition.Name, StringComparer.Ordinal)
            .ToArray();

        ValidateDefinitions(definitions);

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
            nowUtc,
            cancellationToken);

        await EnsureRoleAsync(
            UserRoleName,
            "User",
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
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Role> EnsureRoleAsync(
        string name,
        string displayName,
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

    private static void ValidateDefinitions(
        IReadOnlyCollection<PermissionDefinition> definitions)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Name)
                || string.IsNullOrWhiteSpace(definition.Module)
                || string.IsNullOrWhiteSpace(definition.Description))
            {
                throw new InvalidOperationException(
                    "Permission definitions must contain a name, module, and description.");
            }

            if (!names.Add(definition.Name))
            {
                throw new InvalidOperationException(
                    $"Permission '{definition.Name}' is declared more than once.");
            }
        }
    }
}
