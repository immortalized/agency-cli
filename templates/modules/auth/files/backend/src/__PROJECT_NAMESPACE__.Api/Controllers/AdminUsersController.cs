using System.IdentityModel.Tokens.Jwt;
using __PROJECT_NAMESPACE__.Api.Authorization;
using __PROJECT_NAMESPACE__.Api.Contracts.Auth;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
public sealed class AdminUsersController(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    ITemporaryPasswordGenerator temporaryPasswordGenerator,
    UserSessionInvalidator sessionInvalidator)
    : ControllerBase
{
    [HttpGet]
    [HasPermission(AuthPermissions.UsersRead)]
    public async Task<ActionResult<IReadOnlyList<AdminUserResponse>>>
        GetAll(CancellationToken cancellationToken)
    {
        var users = await UserQuery()
            .AsNoTracking()
            .OrderBy(user => user.Username)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(ToResponse).ToArray());
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AuthPermissions.UsersRead)]
    public async Task<ActionResult<AdminUserResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await UserQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        return user is null
            ? NotFound()
            : Ok(ToResponse(user));
    }

    [HttpPost]
    [HasPermission(AuthPermissions.UsersCreate)]
    public async Task<ActionResult<AdminCreateUserResponse>> Create(
        AdminCreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var identity = NormalizeIdentity(
            request.Username,
            request.Email);

        if (identity is null)
        {
            return ValidationProblem(ModelState);
        }

        if (await IdentityExistsAsync(
                identity.Value.NormalizedUsername,
                identity.Value.NormalizedEmail,
                null,
                cancellationToken))
        {
            return IdentityConflict();
        }

        var requestedRoles =
            NormalizeRoleNames(request.Roles);

        // Choosing the roles of a new account is privilege assignment, not
        // account creation, so it needs the same permission as changing the
        // roles of an existing account.
        if (requestedRoles.Count > 0
            && !CallerHasPermission(AuthPermissions.UsersAssignRoles))
        {
            return MissingAssignRolesPermission();
        }

        var roles = await ResolveRolesAsync(
            requestedRoles.Count > 0
                ? requestedRoles
                : [
                    AuthNormalizer.NormalizeUsername(
                        PermissionSeeder.UserRoleName)
                ],
            cancellationToken);

        if (roles is null)
        {
            return InvalidRoles();
        }

        var temporaryPassword =
            temporaryPasswordGenerator.Generate();

        var nowUtc = DateTimeOffset.UtcNow;

        var user = new User(
            Guid.NewGuid(),
            roles.Select(role => role.Id).ToArray(),
            identity.Value.Username,
            identity.Value.NormalizedUsername,
            passwordHasher.Hash(temporaryPassword),
            nowUtc,
            true,
            identity.Value.Email,
            identity.Value.NormalizedEmail);

        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var created = await UserQuery()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == user.Id,
                cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            new AdminCreateUserResponse(
                ToResponse(created),
                temporaryPassword));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(AuthPermissions.UsersUpdate)]
    public async Task<ActionResult<AdminUserResponse>> Update(
        Guid id,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await UserQuery()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        var identity = NormalizeIdentity(
            request.Username,
            request.Email);

        if (identity is null)
        {
            return ValidationProblem(ModelState);
        }

        if (await IdentityExistsAsync(
                identity.Value.NormalizedUsername,
                identity.Value.NormalizedEmail,
                id,
                cancellationToken))
        {
            return IdentityConflict();
        }

        var nowUtc = DateTimeOffset.UtcNow;

        user.UpdateProfile(
            identity.Value.Username,
            identity.Value.NormalizedUsername,
            identity.Value.Email,
            identity.Value.NormalizedEmail,
            nowUtc);

        await sessionInvalidator.RevokeRefreshTokensAsync(
            [user.Id],
            "User profile changed.",
            RemoteIpAddress(),
            nowUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await UserQuery()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        return Ok(ToResponse(updated));
    }

    [HttpPut("{id:guid}/roles")]
    [HasPermission(AuthPermissions.UsersAssignRoles)]
    public async Task<ActionResult<AdminUserResponse>> AssignRoles(
        Guid id,
        AssignUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await UserQuery()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (id == CurrentUserId())
        {
            return Conflict(new ProblemDetails
            {
                Title =
                    "Administrators cannot change their own role assignment.",
                Detail =
                    "Ask another administrator to make this change so an account cannot escalate or lock out itself.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var roles = await ResolveRolesAsync(
            NormalizeRoleNames(request.Roles),
            cancellationToken);

        if (roles is null)
        {
            return InvalidRoles();
        }

        var nowUtc = DateTimeOffset.UtcNow;

        var changed = user.ReplaceRoles(
            roles.Select(role => role.Id).ToArray(),
            nowUtc);

        if (changed)
        {
            // ReplaceRoles already incremented the auth version, so the
            // outstanding refresh tokens are all that is left to retire.
            await sessionInvalidator.RevokeRefreshTokensAsync(
                [user.Id],
                "User role assignment changed.",
                RemoteIpAddress(),
                nowUtc,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var updated = await UserQuery()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        return Ok(ToResponse(updated));
    }

    [HttpPost("{id:guid}/disable")]
    [HasPermission(AuthPermissions.UsersDisable)]
    public async Task<IActionResult> Disable(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (id == CurrentUserId())
        {
            return Conflict(new ProblemDetails
            {
                Title = "Administrators cannot disable their own account.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var nowUtc = DateTimeOffset.UtcNow;
        user.Disable(nowUtc);

        await sessionInvalidator.RevokeRefreshTokensAsync(
            [id],
            "User disabled.",
            RemoteIpAddress(),
            nowUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/enable")]
    [HasPermission(AuthPermissions.UsersDisable)]
    public async Task<IActionResult> Enable(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        user.Enable(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(AuthPermissions.UsersUpdate)]
    public async Task<ActionResult<AdminResetPasswordResponse>>
        ResetPassword(
            Guid id,
            CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        var temporaryPassword =
            temporaryPasswordGenerator.Generate();

        var nowUtc = DateTimeOffset.UtcNow;

        user.SetTemporaryPassword(
            passwordHasher.Hash(temporaryPassword),
            nowUtc);

        await sessionInvalidator.RevokeRefreshTokensAsync(
            [id],
            "Password reset by administrator.",
            RemoteIpAddress(),
            nowUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new AdminResetPasswordResponse(
            temporaryPassword,
            true));
    }

    private IQueryable<User> UserQuery() =>
        dbContext.Set<User>()
            .Include(user => user.UserRoles)
                .ThenInclude(assignment => assignment.Role)
                    .ThenInclude(role => role.RolePermissions)
                        .ThenInclude(mapping => mapping.Permission);

    private static IReadOnlyList<string> NormalizeRoleNames(
        IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return [];
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(AuthNormalizer.NormalizeUsername)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Resolves normalized role names to active roles. Returns <c>null</c>
    /// when any requested role is missing or inactive, so a typo can never
    /// silently strip a user's authorization.
    /// </summary>
    private async Task<IReadOnlyList<Role>?> ResolveRolesAsync(
        IReadOnlyList<string> normalizedNames,
        CancellationToken cancellationToken)
    {
        if (normalizedNames.Count == 0)
        {
            // An explicitly empty set is valid: the user keeps their account
            // but holds no permissions.
            return [];
        }

        var roles = await dbContext.Set<Role>()
            .Where(role =>
                normalizedNames.Contains(role.NormalizedName)
                && role.IsActive)
            .ToListAsync(cancellationToken);

        return roles.Count == normalizedNames.Count
            ? roles
            : null;
    }

    private async Task<bool> IdentityExistsAsync(
        string normalizedUsername,
        string? normalizedEmail,
        Guid? excludedUserId,
        CancellationToken cancellationToken) =>
        await dbContext.Set<User>().AnyAsync(
            user =>
                (!excludedUserId.HasValue
                    || user.Id != excludedUserId.Value)
                && (user.NormalizedUsername == normalizedUsername
                    || (normalizedEmail != null
                        && user.NormalizedEmail == normalizedEmail)),
            cancellationToken);

    private (
        string Username,
        string NormalizedUsername,
        string? Email,
        string? NormalizedEmail)? NormalizeIdentity(
        string username,
        string? email)
    {
        var trimmedUsername = username.Trim();

        if (trimmedUsername.Length == 0)
        {
            ModelState.AddModelError(
                nameof(username),
                "Username cannot be empty.");
            return null;
        }

        var trimmedEmail = string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim();

        return (
            trimmedUsername,
            AuthNormalizer.NormalizeUsername(trimmedUsername),
            trimmedEmail,
            AuthNormalizer.NormalizeEmail(trimmedEmail));
    }

    private bool CallerHasPermission(string permission) =>
        User.HasClaim(
            AuthClaimNames.Permission,
            permission);

    private string? RemoteIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    private Guid CurrentUserId()
    {
        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(subject, out var userId)
            ? userId
            : Guid.Empty;
    }

    private static AdminUserResponse ToResponse(User user) =>
        new(
            user.Id,
            user.Username,
            user.Email,
            user.UserRoles
                .Select(assignment => assignment.Role.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            user.UserRoles
                .SelectMany(assignment =>
                    assignment.Role.RolePermissions)
                .Select(mapping => mapping.Permission.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => permission, StringComparer.Ordinal)
                .ToArray(),
            user.IsActive,
            user.MustChangePassword,
            user.CreatedAtUtc,
            user.UpdatedAtUtc,
            user.LastLoginAtUtc,
            user.PasswordChangedAtUtc,
            user.DisabledAtUtc);

    private static ConflictObjectResult IdentityConflict() =>
        new(new ProblemDetails
        {
            Title = "Username or email is already in use.",
            Status = StatusCodes.Status409Conflict
        });

    private static BadRequestObjectResult InvalidRoles() =>
        new(new ProblemDetails
        {
            Title = "One or more selected roles are invalid or inactive.",
            Detail =
                "GET /api/admin/roles lists every assignable role.",
            Status = StatusCodes.Status400BadRequest
        });

    private static ObjectResult MissingAssignRolesPermission() =>
        new(new ProblemDetails
        {
            Title =
                $"The '{AuthPermissions.UsersAssignRoles}' permission is required to choose roles.",
            Detail =
                "Create the account without roles, or ask for the role-assignment permission.",
            Status = StatusCodes.Status403Forbidden
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
}
