using __PROJECT_NAMESPACE__.Api.Contracts.Auth;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Application.Auth.Models;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    BootstrapSecretValidator bootstrapSecretValidator,
    IOptions<AuthOptions> authOptions)
    : ControllerBase
{
    private const string BootstrapHeaderName =
        "X-Bootstrap-Secret";

    [AllowAnonymous]
    [HttpPost("bootstrap")]
    public async Task<ActionResult<AuthResponse>> Bootstrap(
        BootstrapRequest request,
        CancellationToken cancellationToken)
    {
        var suppliedSecret =
            Request.Headers[BootstrapHeaderName]
                .FirstOrDefault();

        if (!bootstrapSecretValidator.IsValid(
                suppliedSecret))
        {
            return Unauthorized();
        }

        // A tranzakció és a DB constraint együtt védi a
        // párhuzamos bootstrap kéréseket.
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        if (await dbContext
                .Set<User>()
                .AnyAsync(cancellationToken))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Bootstrap is no longer available.",
                Detail =
                    "At least one user account already exists.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var username = request.Username.Trim();
        var normalizedUsername =
            AuthNormalizer.NormalizeUsername(username);

        var email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : request.Email.Trim();

        var normalizedEmail =
            AuthNormalizer.NormalizeEmail(email);

        var passwordHash =
            passwordHasher.Hash(request.Password);

        var nowUtc = DateTimeOffset.UtcNow;

        var user = new User(
            Guid.NewGuid(),
            username,
            normalizedUsername,
            passwordHash,
            nowUtc,
            email,
            normalizedEmail);

        dbContext.Set<User>().Add(user);

        var response = CreateAuthenticatedSession(
            user,
            nowUtc);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            response);
    }

    [AllowAnonymous]
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

        var user = await dbContext
            .Set<User>()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.NormalizedUsername
                        == normalizedUsername ||
                    candidate.NormalizedEmail
                        == normalizedEmail,
                cancellationToken);

        // Ugyanazt a választ adjuk nem létező user,
        // hibás jelszó és letiltott user esetén.
        if (user is null || !user.IsActive)
        {
            return InvalidCredentials();
        }

        var passwordResult = passwordHasher.Verify(
            request.Password,
            user.PasswordHash);

        if (passwordResult
            == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        var nowUtc = DateTimeOffset.UtcNow;

        if (passwordResult
            == PasswordVerificationResult
                .SuccessRehashNeeded)
        {
            user.ChangePassword(
                passwordHasher.Hash(request.Password),
                nowUtc);
        }

        user.RecordSuccessfulLogin(nowUtc);

        var response = CreateAuthenticatedSession(
            user,
            nowUtc);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return Ok(response);
    }

    private AuthResponse CreateAuthenticatedSession(
        User user,
        DateTimeOffset nowUtc)
    {
        var accessToken =
            accessTokenService.Create(
                new AccessTokenSubject(
                    user.Id,
                    user.Username,
                    user.Email));

        var refreshToken =
            refreshTokenService.Create();

        var refreshTokenEntity = new RefreshToken(
            Guid.NewGuid(),
            user.Id,
            Guid.NewGuid(),
            refreshToken.TokenHash,
            nowUtc,
            nowUtc.AddDays(
                authOptions.Value
                    .RefreshTokenLifetimeDays),
            HttpContext.Connection
                .RemoteIpAddress?
                .ToString(),
            Request.Headers.UserAgent
                .FirstOrDefault());

        dbContext
            .Set<RefreshToken>()
            .Add(refreshTokenEntity);

        WriteRefreshCookie(
            refreshToken.PlainTextToken,
            refreshTokenEntity.ExpiresAtUtc);

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            new AuthUserResponse(
                user.Id,
                user.Username,
                user.Email));
    }

    private void WriteRefreshCookie(
        string token,
        DateTimeOffset expiresAtUtc)
    {
        Response.Cookies.Append(
            authOptions.Value.RefreshCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = expiresAtUtc,
                IsEssential = true
            });
    }

    private static UnauthorizedObjectResult
        InvalidCredentials()
    {
        return new UnauthorizedObjectResult(
            new ProblemDetails
            {
                Title = "Invalid credentials.",
                Detail =
                    "The supplied credentials are invalid.",
                Status =
                    StatusCodes.Status401Unauthorized
            });
    }
}