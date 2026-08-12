using __PROJECT_NAMESPACE__.Api.Authorization;
using __PROJECT_NAMESPACE__.Api.Contracts.Roles;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[Route("api/admin/roles")]
public sealed class AdminRolesController(
    AppDbContext dbContext,
    PermissionCatalog permissionCatalog,
    UserSessionInvalidator sessionInvalidator)
    : ControllerBase
{
    [HttpGet]
    [HasPermission(AuthPermissions.RolesRead)]
    public async Task<ActionResult<IReadOnlyList<RoleResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var roles = await RoleQuery()
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);

        var memberCounts = await GetMemberCountsAsync(
            cancellationToken);

        return Ok(roles
            .Select(role => ToResponse(role, memberCounts))
            .ToArray());
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AuthPermissions.RolesRead)]
    public async Task<ActionResult<RoleResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var role = await RoleQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (role is null)
        {
            return NotFound();
        }

        var memberCounts = await GetMemberCountsAsync(
            cancellationToken);

        return Ok(ToResponse(role, memberCounts));
    }

    [HttpPost]
    [HasPermission(AuthPermissions.RolesCreate)]
    public async Task<ActionResult<RoleResponse>> Create(
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            ModelState.AddModelError(
                nameof(request.Name),
                "Role name cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var normalizedName =
            AuthNormalizer.NormalizeUsername(name);

        if (await dbContext.Set<Role>().AnyAsync(
                role => role.NormalizedName == normalizedName,
                cancellationToken))
        {
            return NameConflict(name);
        }

        var resolved = await ResolvePermissionsAsync(
            request.Permissions,
            cancellationToken);

        if (resolved.Error is not null)
        {
            return resolved.Error;
        }

        var nowUtc = DateTimeOffset.UtcNow;

        var role = new Role(
            Guid.NewGuid(),
            name,
            normalizedName,
            string.IsNullOrWhiteSpace(request.DisplayName)
                ? name
                : request.DisplayName.Trim(),
            request.Description,
            false,
            nowUtc);

        dbContext.Set<Role>().Add(role);

        foreach (var permission in resolved.Permissions)
        {
            dbContext.Set<RolePermission>().Add(
                new RolePermission(
                    role.Id,
                    permission.Id,
                    nowUtc));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // A brand-new role has no members, so no session invalidation is
        // needed here.
        var created = await RoleQuery()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == role.Id,
                cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = role.Id },
            ToResponse(created, new Dictionary<Guid, int>()));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(AuthPermissions.RolesUpdate)]
    public async Task<ActionResult<RoleResponse>> Update(
        Guid id,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = await RoleQuery()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (role is null)
        {
            return NotFound();
        }

        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            ModelState.AddModelError(
                nameof(request.Name),
                "Role name cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var normalizedName =
            AuthNormalizer.NormalizeUsername(name);

        var renamed = !string.Equals(
            role.NormalizedName,
            normalizedName,
            StringComparison.Ordinal);

        if (renamed && role.IsSystem)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Built-in roles cannot be renamed.",
                Detail =
                    $"Role '{role.Name}' ships with the application. Its display name and description can still be changed.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (renamed && await dbContext.Set<Role>().AnyAsync(
                candidate => candidate.NormalizedName == normalizedName
                    && candidate.Id != id,
                cancellationToken))
        {
            return NameConflict(name);
        }

        var nowUtc = DateTimeOffset.UtcNow;

        var permissionSetManaged =
            PermissionSeeder.IsPermissionSetManaged(role);

        var currentPermissionNames = role.RolePermissions
            .Select(mapping => mapping.Permission.Name)
            .ToHashSet(StringComparer.Ordinal);

        var permissionSetChanged = false;

        if (request.Permissions is not null)
        {
            var resolved = await ResolvePermissionsAsync(
                request.Permissions,
                cancellationToken);

            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var desiredNames = resolved.Permissions
                .Select(permission => permission.Name)
                .ToHashSet(StringComparer.Ordinal);

            permissionSetChanged =
                !desiredNames.SetEquals(currentPermissionNames);

            if (permissionSetChanged && permissionSetManaged)
            {
                return Conflict(new ProblemDetails
                {
                    Title =
                        "The built-in administrator permission set is managed by module seeding.",
                    Detail =
                        "It always holds every installed permission and is re-granted on every startup. Create a custom role instead.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            if (permissionSetChanged)
            {
                ReplacePermissions(
                    role,
                    resolved.Permissions,
                    nowUtc);
            }
        }

        role.UpdateDetails(
            name,
            normalizedName,
            // Omitting the display name keeps the current one, so a caller
            // that only edits permissions does not silently overwrite it.
            string.IsNullOrWhiteSpace(request.DisplayName)
                ? role.DisplayName
                : request.DisplayName.Trim(),
            request.Description,
            nowUtc);

        if (permissionSetChanged)
        {
            // Every member's effective permissions just changed, so their
            // already-issued access tokens no longer describe them.
            var memberIds = await sessionInvalidator
                .GetRoleMemberIdsAsync(id, cancellationToken);

            await sessionInvalidator.InvalidateAsync(
                memberIds,
                $"Permission set of role '{role.Name}' changed.",
                RemoteIpAddress(),
                nowUtc,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await RoleQuery()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        var memberCounts = await GetMemberCountsAsync(
            cancellationToken);

        return Ok(ToResponse(updated, memberCounts));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(AuthPermissions.RolesDelete)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Set<Role>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystem)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Built-in roles cannot be deleted.",
                Detail =
                    $"Role '{role.Name}' ships with the application and is required by the seeding and registration flows.",
                Status = StatusCodes.Status409Conflict
            });
        }

        // Members are never silently reassigned: the caller decides where they
        // belong and moves them explicitly first.
        var memberCount = await dbContext.Set<UserRole>()
            .CountAsync(
                assignment => assignment.RoleId == id,
                cancellationToken);

        if (memberCount > 0)
        {
            return Conflict(new ProblemDetails
            {
                Title = "The role still has members.",
                Detail =
                    $"{memberCount} user(s) are assigned role '{role.Name}'. Reassign them with PUT /api/admin/users/{{id}}/roles before deleting it.",
                Status = StatusCodes.Status409Conflict
            });
        }

        dbContext.Set<Role>().Remove(role);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private IQueryable<Role> RoleQuery() =>
        dbContext.Set<Role>()
            .Include(role => role.RolePermissions)
                .ThenInclude(mapping => mapping.Permission);

    private async Task<Dictionary<Guid, int>> GetMemberCountsAsync(
        CancellationToken cancellationToken) =>
        await dbContext.Set<UserRole>()
            .GroupBy(assignment => assignment.RoleId)
            .Select(group => new
            {
                RoleId = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(
                entry => entry.RoleId,
                entry => entry.Count,
                cancellationToken);

    private void ReplacePermissions(
        Role role,
        IReadOnlyCollection<Permission> permissions,
        DateTimeOffset nowUtc)
    {
        var desiredIds = permissions
            .Select(permission => permission.Id)
            .ToHashSet();

        var removed = role.RolePermissions
            .Where(mapping => !desiredIds.Contains(mapping.PermissionId))
            .ToArray();

        dbContext.Set<RolePermission>().RemoveRange(removed);

        var currentIds = role.RolePermissions
            .Select(mapping => mapping.PermissionId)
            .ToHashSet();

        foreach (var permissionId in desiredIds
                     .Where(permissionId =>
                         !currentIds.Contains(permissionId)))
        {
            dbContext.Set<RolePermission>().Add(
                new RolePermission(
                    role.Id,
                    permissionId,
                    nowUtc));
        }
    }

    private async Task<(
        IReadOnlyList<Permission> Permissions,
        ActionResult? Error)> ResolvePermissionsAsync(
        IReadOnlyList<string>? requested,
        CancellationToken cancellationToken)
    {
        var names = PermissionCatalog.Normalize(requested);

        if (names.Count == 0)
        {
            return ([], null);
        }

        var unregistered =
            permissionCatalog.FindUnregistered(names);

        if (unregistered.Count > 0)
        {
            return ([], UnknownPermissions(unregistered));
        }

        var permissions = await dbContext.Set<Permission>()
            .Where(permission => names.Contains(permission.Name))
            .ToListAsync(cancellationToken);

        // The catalog knows the name but seeding has not stored it yet. This
        // should not happen because seeding runs as a startup task before the
        // server accepts requests, so report it instead of silently dropping
        // the permission from the role.
        if (permissions.Count != names.Count)
        {
            var stored = permissions
                .Select(permission => permission.Name)
                .ToHashSet(StringComparer.Ordinal);

            return ([], Conflict(new ProblemDetails
            {
                Title = "The permission catalog is not fully seeded.",
                Detail =
                    $"Registered but unseeded: {string.Join(", ", names.Where(name => !stored.Contains(name)))}. Restart the API to complete permission seeding.",
                Status = StatusCodes.Status409Conflict
            }));
        }

        return (permissions, null);
    }

    private string? RemoteIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    private static RoleResponse ToResponse(
        Role role,
        IReadOnlyDictionary<Guid, int> memberCounts) =>
        new(
            role.Id,
            role.Name,
            role.DisplayName,
            role.Description,
            role.IsSystem,
            role.IsActive,
            PermissionSeeder.IsPermissionSetManaged(role),
            role.RolePermissions
                .Select(mapping => mapping.Permission.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            memberCounts.TryGetValue(role.Id, out var count)
                ? count
                : 0,
            role.CreatedAtUtc,
            role.UpdatedAtUtc);

    private ConflictObjectResult NameConflict(string name) =>
        Conflict(new ProblemDetails
        {
            Title = "A role with that name already exists.",
            Detail = $"Role name '{name}' is already in use.",
            Status = StatusCodes.Status409Conflict
        });

    private BadRequestObjectResult UnknownPermissions(
        IReadOnlyCollection<string> unknown) =>
        BadRequest(new ProblemDetails
        {
            Title = "Unknown permissions.",
            Detail =
                $"No installed module registers: {string.Join(", ", unknown)}. Permission names are case-sensitive; GET /api/admin/permissions lists every valid key.",
            Status = StatusCodes.Status400BadRequest
        });
}
