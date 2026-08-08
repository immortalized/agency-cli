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
    ITemporaryPasswordGenerator temporaryPasswordGenerator)
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

        var role = await FindRoleAsync(
            request.Role ?? PermissionSeeder.UserRoleName,
            cancellationToken);

        if (role is null)
        {
            return InvalidRole();
        }

        var temporaryPassword =
            temporaryPasswordGenerator.Generate();

        var nowUtc = DateTimeOffset.UtcNow;

        var user = new User(
            Guid.NewGuid(),
            role.Id,
            identity.Value.Username,
            identity.Value.NormalizedUsername,
            passwordHasher.Hash(temporaryPassword),
            nowUtc,
            true,
            identity.Value.Email,
            identity.Value.NormalizedEmail);

        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        user = await UserQuery()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == user.Id,
                cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = user.Id },
            new AdminCreateUserResponse(
                ToResponse(user),
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

        var role = await FindRoleAsync(
            request.Role,
            cancellationToken);

        if (role is null)
        {
            return InvalidRole();
        }

        var nowUtc = DateTimeOffset.UtcNow;

        user.UpdateProfile(
            identity.Value.Username,
            identity.Value.NormalizedUsername,
            identity.Value.Email,
            identity.Value.NormalizedEmail,
            nowUtc);

        user.ChangeRole(role.Id, nowUtc);

        await RevokeUserTokensAsync(
            user.Id,
            "User authorization data changed.",
            nowUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        user = await UserQuery()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        return Ok(ToResponse(user));
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

        await RevokeUserTokensAsync(
            id,
            "User disabled.",
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

        await RevokeUserTokensAsync(
            id,
            "Password reset by administrator.",
            nowUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new AdminResetPasswordResponse(
            temporaryPassword,
            true));
    }

    private IQueryable<User> UserQuery() =>
        dbContext.Set<User>()
            .Include(user => user.Role)
                .ThenInclude(role => role.RolePermissions)
                    .ThenInclude(mapping => mapping.Permission);

    private async Task<Role?> FindRoleAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return null;
        }

        var normalized = AuthNormalizer.NormalizeUsername(roleName);

        return await dbContext.Set<Role>()
            .SingleOrDefaultAsync(
                role => role.NormalizedName == normalized
                    && role.IsActive,
                cancellationToken);
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

    private async Task RevokeUserTokensAsync(
        Guid userId,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var tokens = await dbContext.Set<RefreshToken>()
            .Where(token => token.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens.Where(
                     token => token.IsActive(nowUtc)))
        {
            token.Revoke(
                nowUtc,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                reason);
        }
    }

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
            user.Role.Name,
            user.Role.RolePermissions
                .Select(mapping => mapping.Permission.Name)
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

    private static BadRequestObjectResult InvalidRole() =>
        new(new ProblemDetails
        {
            Title = "The selected role is invalid or inactive.",
            Status = StatusCodes.Status400BadRequest
        });
}
