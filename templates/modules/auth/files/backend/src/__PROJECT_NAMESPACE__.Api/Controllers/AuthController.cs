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
    IOptions<AuthOptions> authOptions)
    : ControllerBase
{
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

        // The same response is returned for an unknown user,
        // an incorrect password and a disabled account.
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