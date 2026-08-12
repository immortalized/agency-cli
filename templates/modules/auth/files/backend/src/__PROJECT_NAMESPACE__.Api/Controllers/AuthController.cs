using System.IdentityModel.Tokens.Jwt;
using __PROJECT_NAMESPACE__.Api.Authorization;
using __PROJECT_NAMESPACE__.Api.Contracts.Auth;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Application.Auth.Models;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IAccessTokenService accessTokenService,
    IRefreshTokenService refreshTokenService,
    UserSessionInvalidator sessionInvalidator,
    IOptions<AuthOptions> authOptions)
    : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicy.Name)]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var identifier = request.Identifier.Trim();

        var normalizedUsername =
            AuthNormalizer.NormalizeUsername(identifier);

        var normalizedEmail =
            AuthNormalizer.NormalizeEmail(identifier);

        var user = await UserWithAuthorization()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.NormalizedUsername == normalizedUsername
                    || candidate.NormalizedEmail == normalizedEmail,
                cancellationToken);

        if (user is null || !user.IsActive)
        {
            return InvalidCredentials();
        }

        var passwordResult = passwordHasher.Verify(
            request.Password,
            user.PasswordHash);

        if (passwordResult == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        var nowUtc = DateTimeOffset.UtcNow;

        if (passwordResult
            == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.RehashPassword(
                passwordHasher.Hash(request.Password),
                nowUtc);
        }

        user.RecordSuccessfulLogin(nowUtc);

        var response = await CreateAuthenticatedSessionAsync(
            user,
            Guid.NewGuid(),
            nowUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(response);
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicy.Name)]
    [HttpPost("register")]
    public async Task<ActionResult<AuthUserResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (authOptions.Value.RegistrationMode
            != RegistrationMode.Public)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ProblemDetails
                {
                    Title = "Registration is not enabled.",
                    Detail =
                        "Accounts must be created by an administrator.",
                    Status = StatusCodes.Status403Forbidden
                });
        }

        var username = request.Username.Trim();
        var email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : request.Email.Trim();

        if (username.Length == 0)
        {
            ModelState.AddModelError(
                nameof(request.Username),
                "Username cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var normalizedUsername =
            AuthNormalizer.NormalizeUsername(username);

        var normalizedEmail =
            AuthNormalizer.NormalizeEmail(email);

        if (await UserIdentityExistsAsync(
                normalizedUsername,
                normalizedEmail,
                null,
                cancellationToken))
        {
            return IdentityConflict();
        }

        var userRoleName = AuthNormalizer.NormalizeUsername(
            PermissionSeeder.UserRoleName);

        var role = await dbContext.Set<Role>()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.NormalizedName == userRoleName
                    && candidate.IsActive,
                cancellationToken);

        if (role is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Registration is temporarily unavailable.",
                    Detail =
                        $"The built-in '{PermissionSeeder.UserRoleName}' role is missing or inactive; permission seeding has not completed.",
                    Status = StatusCodes.Status503ServiceUnavailable
                });
        }

        string passwordHash;

        try
        {
            passwordHash = passwordHasher.Hash(request.Password);
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(
                nameof(request.Password),
                exception.Message);
            return ValidationProblem(ModelState);
        }

        var nowUtc = DateTimeOffset.UtcNow;

        var user = new User(
            Guid.NewGuid(),
            [role.Id],
            username,
            normalizedUsername,
            passwordHash,
            nowUtc,
            false,
            email,
            normalizedEmail);

        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Registration intentionally does not create a session. The new
        // account follows the same normal login flow as every other user.
        return StatusCode(
            StatusCodes.Status201Created,
            ToUserResponse(user, [role.Name], []));
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicy.Name)]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(
                authOptions.Value.RefreshCookieName,
                out var plainTextToken)
            || string.IsNullOrWhiteSpace(plainTextToken))
        {
            return InvalidRefreshToken();
        }

        byte[] tokenHash;

        try
        {
            tokenHash = refreshTokenService.Hash(plainTextToken);
        }
        catch (Exception exception)
            when (exception is FormatException
                  or ArgumentException)
        {
            ClearRefreshCookie();
            return InvalidRefreshToken();
        }

        var storedToken = await dbContext.Set<RefreshToken>()
            .Include(token => token.User)
                .ThenInclude(user => user.UserRoles)
                    .ThenInclude(assignment => assignment.Role)
                        .ThenInclude(role => role.RolePermissions)
                            .ThenInclude(mapping => mapping.Permission)
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash,
                cancellationToken);

        if (storedToken is null)
        {
            ClearRefreshCookie();
            return InvalidRefreshToken();
        }

        var nowUtc = DateTimeOffset.UtcNow;

        if (!storedToken.IsActive(nowUtc))
        {
            await sessionInvalidator.RevokeFamilyAsync(
                storedToken.FamilyId,
                "Refresh token reuse detected.",
                RemoteIpAddress(),
                nowUtc,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            ClearRefreshCookie();
            return InvalidRefreshToken();
        }

        if (!storedToken.User.IsActive)
        {
            await sessionInvalidator.RevokeRefreshTokensAsync(
                [storedToken.UserId],
                "User is disabled.",
                RemoteIpAddress(),
                nowUtc,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            ClearRefreshCookie();
            return InvalidRefreshToken();
        }

        var replacementId = Guid.NewGuid();
        storedToken.MarkAsUsed(replacementId, nowUtc);

        var response = await CreateAuthenticatedSessionAsync(
            storedToken.User,
            storedToken.FamilyId,
            nowUtc,
            cancellationToken,
            replacementId);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(
                authOptions.Value.RefreshCookieName,
                out var plainTextToken)
            && !string.IsNullOrWhiteSpace(plainTextToken))
        {
            byte[]? tokenHash = null;

            try
            {
                tokenHash = refreshTokenService.Hash(plainTextToken);
            }
            catch (Exception exception)
                when (exception is FormatException
                      or ArgumentException)
            {
                // Invalid cookies are cleared below.
            }

            if (tokenHash is not null)
            {
                var storedToken = await dbContext
                    .Set<RefreshToken>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        token => token.TokenHash == tokenHash,
                        cancellationToken);

                if (storedToken is not null)
                {
                    await sessionInvalidator.RevokeFamilyAsync(
                        storedToken.FamilyId,
                        "User logout.",
                        RemoteIpAddress(),
                        DateTimeOffset.UtcNow,
                        cancellationToken);

                    await dbContext.SaveChangesAsync(
                        cancellationToken);
                }
            }
        }

        ClearRefreshCookie();
        return NoContent();
    }

    [Authorize(Policy = AuthPolicies.PasswordChangeAllowed)]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserResponse>> Me(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var user = await UserWithAuthorization()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == userId,
                cancellationToken);

        return Ok(ToUserResponse(user));
    }

    [Authorize(Policy = AuthPolicies.PasswordChangeAllowed)]
    [EnableRateLimiting(AuthRateLimitPolicy.Name)]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var user = await dbContext.Set<User>()
            .SingleAsync(
                candidate => candidate.Id == userId,
                cancellationToken);

        if (passwordHasher.Verify(
                request.CurrentPassword,
                user.PasswordHash)
            == PasswordVerificationResult.Failed)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Current password is invalid.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (passwordHasher.Verify(
                request.NewPassword,
                user.PasswordHash)
            != PasswordVerificationResult.Failed)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The new password must be different.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string passwordHash;

        try
        {
            passwordHash = passwordHasher.Hash(request.NewPassword);
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(
                nameof(request.NewPassword),
                exception.Message);
            return ValidationProblem(ModelState);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        user.ChangePassword(passwordHash, nowUtc);

        await sessionInvalidator.RevokeRefreshTokensAsync(
            [user.Id],
            "Password changed.",
            RemoteIpAddress(),
            nowUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        ClearRefreshCookie();

        return NoContent();
    }

    private IQueryable<User> UserWithAuthorization() =>
        dbContext.Set<User>()
            .Include(user => user.UserRoles)
                .ThenInclude(assignment => assignment.Role)
                    .ThenInclude(role => role.RolePermissions)
                        .ThenInclude(mapping => mapping.Permission);

    private async Task<AuthResponse> CreateAuthenticatedSessionAsync(
        User user,
        Guid familyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        Guid? tokenId = null)
    {
        var roles = GetRoleNames(user);
        var permissions = GetEffectivePermissions(user);

        var accessToken = await accessTokenService.CreateAsync(
            new AccessTokenSubject(
                user.Id,
                user.Username,
                user.Email,
                roles,
                permissions,
                user.AuthVersion,
                user.MustChangePassword),
            cancellationToken);

        var refreshToken = refreshTokenService.Create();

        var refreshTokenEntity = new RefreshToken(
            tokenId ?? Guid.NewGuid(),
            user.Id,
            familyId,
            refreshToken.TokenHash,
            nowUtc,
            nowUtc.AddDays(
                authOptions.Value.RefreshTokenLifetimeDays),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.FirstOrDefault());

        dbContext.Set<RefreshToken>().Add(refreshTokenEntity);

        WriteRefreshCookie(
            refreshToken.PlainTextToken,
            refreshTokenEntity.ExpiresAtUtc);

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            ToUserResponse(user, roles, permissions));
    }

    private static IReadOnlyList<string> GetRoleNames(User user) =>
        user.UserRoles
            .Select(assignment => assignment.Role.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The union of every assigned role's permissions, or nothing at all
    /// while a forced password change is pending.
    /// </summary>
    private static IReadOnlyList<string> GetEffectivePermissions(
        User user)
    {
        if (user.MustChangePassword)
        {
            return [];
        }

        return user.UserRoles
            .SelectMany(assignment =>
                assignment.Role.RolePermissions)
            .Select(mapping => mapping.Permission.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();
    }

    private static AuthUserResponse ToUserResponse(User user) =>
        ToUserResponse(
            user,
            GetRoleNames(user),
            GetEffectivePermissions(user));

    private static AuthUserResponse ToUserResponse(
        User user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions) =>
        new(
            user.Id,
            user.Username,
            user.Email,
            roles,
            permissions,
            user.MustChangePassword);

    private string? RemoteIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<bool> UserIdentityExistsAsync(
        string normalizedUsername,
        string? normalizedEmail,
        Guid? excludedUserId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<User>().AnyAsync(
            user =>
                (!excludedUserId.HasValue
                    || user.Id != excludedUserId.Value)
                && (user.NormalizedUsername == normalizedUsername
                    || (normalizedEmail != null
                        && user.NormalizedEmail == normalizedEmail)),
            cancellationToken);
    }

    private Guid GetCurrentUserId()
    {
        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            throw new InvalidOperationException(
                "The authenticated user identifier is invalid.");
        }

        return userId;
    }

    private void WriteRefreshCookie(
        string token,
        DateTimeOffset expiresAtUtc)
    {
        Response.Cookies.Append(
            authOptions.Value.RefreshCookieName,
            token,
            CreateCookieOptions(expiresAtUtc));
    }

    private void ClearRefreshCookie()
    {
        Response.Cookies.Delete(
            authOptions.Value.RefreshCookieName,
            CreateCookieOptions(null));
    }

    private static CookieOptions CreateCookieOptions(
        DateTimeOffset? expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAtUtc,
        IsEssential = true
    };

    private static ConflictObjectResult IdentityConflict() =>
        new(new ProblemDetails
        {
            Title = "Username or email is already in use.",
            Status = StatusCodes.Status409Conflict
        });

    private static UnauthorizedObjectResult InvalidCredentials() =>
        new(new ProblemDetails
        {
            Title = "Invalid credentials.",
            Detail = "The supplied credentials are invalid.",
            Status = StatusCodes.Status401Unauthorized
        });

    private static UnauthorizedObjectResult InvalidRefreshToken() =>
        new(new ProblemDetails
        {
            Title = "Invalid refresh token.",
            Status = StatusCodes.Status401Unauthorized
        });
}
