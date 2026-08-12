using System.Data;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Operations;

public sealed class InitialAdminCreator(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher)
{
    public async Task<InitialAdminResult> CreateAsync(
        string username,
        string? email,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(username, email);

        var trimmedUsername = username.Trim();

        var trimmedEmail =
            string.IsNullOrWhiteSpace(email)
                ? null
                : email.Trim();

        var normalizedUsername =
            AuthNormalizer.NormalizeUsername(
                trimmedUsername);

        var normalizedEmail =
            AuthNormalizer.NormalizeEmail(
                trimmedEmail);

        // Seed from every permission provider the installed modules ship, not
        // just the auth module's own, so the built-in administrator role holds
        // the complete installed permission set the moment it is created.
        var permissionSeeder = new PermissionSeeder(
            dbContext,
            PermissionDefinitionProviderLoader.CreateCatalog());

        await permissionSeeder.SeedAsync(
            cancellationToken);

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(6843229417055901771)",
            cancellationToken);

        if (await dbContext.Set<User>()
                .AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The initial administrator can only be created when no users exist.");
        }

        var normalizedRoleName =
            AuthNormalizer.NormalizeUsername(
                PermissionSeeder.AdministratorRoleName);

        // Seeding above already created this role and granted it every
        // installed permission, so it is only looked up here.
        var role = await dbContext.Set<Role>()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.NormalizedName ==
                    normalizedRoleName,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Permission seeding did not create the built-in administrator role.");

        if (!role.IsSystem || !role.IsActive)
        {
            throw new InvalidOperationException(
                "The existing administrator role is not a valid active system role.");
        }

        var nowUtc = DateTimeOffset.UtcNow;

        var temporaryPassword =
            TemporaryPasswordGenerator.Generate();

        var passwordHash =
            passwordHasher.Hash(
                temporaryPassword);

        var user = new User(
            Guid.NewGuid(),
            [role.Id],
            trimmedUsername,
            normalizedUsername,
            passwordHash,
            nowUtc,
            true,
            trimmedEmail,
            normalizedEmail);

        dbContext.Set<User>().Add(user);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return new InitialAdminResult(
            user.Id,
            user.Username,
            user.Email,
            temporaryPassword);
    }

    private static void ValidateInput(
        string username,
        string? email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            username);

        var trimmedUsername = username.Trim();

        if (trimmedUsername.Length > 64)
        {
            throw new ArgumentException(
                "Username cannot exceed 64 characters.",
                nameof(username));
        }

        if (email is not null &&
            email.Trim().Length > 320)
        {
            throw new ArgumentException(
                "Email cannot exceed 320 characters.",
                nameof(email));
        }
    }
}
