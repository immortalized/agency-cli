using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using __PROJECT_NAMESPACE__.Application.Auth;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using __PROJECT_NAMESPACE__.Domain.Auth;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;

// Focused, offline validation of the role and permission logic. Needs no
// PostgreSQL and no OpenBao; run it with:
//
//   dotnet run --project backend/validation/__PROJECT_NAMESPACE__.Auth.Validation/__PROJECT_NAMESPACE__.Auth.Validation.csproj

var failures = new List<string>();

void Check(string description, bool condition)
{
    Console.WriteLine($"{(condition ? "pass" : "FAIL")}  {description}");

    if (!condition)
    {
        failures.Add(description);
    }
}

// ---------------------------------------------------------------------------
// Permission catalog: contributed by installed modules, never hardcoded here.
// ---------------------------------------------------------------------------

var catalog = PermissionDefinitionProviderLoader.CreateCatalog();

Check(
    "the catalog exposes every auth-module permission",
    AuthPermissions.All.All(
        definition => catalog.Contains(definition.Name)));

Check(
    "the catalog is sorted ordinally and free of duplicates",
    catalog.Definitions
        .Select(definition => definition.Name)
        .SequenceEqual(catalog.Definitions
            .Select(definition => definition.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)));

Check(
    "every catalog entry declares an owning module",
    catalog.Definitions.All(
        definition => !string.IsNullOrWhiteSpace(definition.Module)));

Check(
    "modules other than auth can contribute permissions",
    catalog.Definitions.Any(
        definition => !string.Equals(
            definition.Module,
            "auth",
            StringComparison.Ordinal))
    || catalog.Definitions.Count == AuthPermissions.All.Count);

// Permission claims are matched ordinally, so a differently cased name must be
// reported as unregistered instead of silently resolving.
var probe = AuthPermissions.UsersRead;

Check(
    "an exactly matching permission name is registered",
    catalog.FindUnregistered([probe]).Count == 0);

Check(
    "an upper-cased permission name is reported as unregistered",
    catalog.FindUnregistered([probe.ToUpperInvariant()])
        .SequenceEqual([probe.ToUpperInvariant()], StringComparer.Ordinal));

Check(
    "a surrounding-whitespace permission name is trimmed, not rejected",
    catalog.FindUnregistered([$"  {probe}  "]).Count == 0);

Check(
    "normalization trims, de-duplicates, and sorts ordinally",
    PermissionCatalog.Normalize(["b", " a ", "a", "", "  "])
        .SequenceEqual(["a", "b"], StringComparer.Ordinal));

Check(
    "a null permission list normalizes to an empty set",
    PermissionCatalog.Normalize(null).Count == 0);

Check(
    "duplicate definitions across providers are rejected",
    Throws<InvalidOperationException>(() => _ = new PermissionCatalog(
    [
        new AuthPermissionDefinitionProvider(),
        new AuthPermissionDefinitionProvider()
    ])));

// ---------------------------------------------------------------------------
// Role invariants.
// ---------------------------------------------------------------------------

var nowUtc = DateTimeOffset.UtcNow;

var builtIn = new Role(
    Guid.NewGuid(),
    PermissionSeeder.AdministratorRoleName,
    AuthNormalizer.NormalizeUsername(
        PermissionSeeder.AdministratorRoleName),
    "Administrator",
    null,
    true,
    nowUtc);

Check(
    "the built-in administrator role owns a seeder-managed permission set",
    PermissionSeeder.IsPermissionSetManaged(builtIn));

Check(
    "renaming a built-in role is refused",
    Throws<InvalidOperationException>(() => builtIn.UpdateDetails(
        "root",
        "ROOT",
        "Root",
        null,
        nowUtc)));

builtIn.UpdateDetails(
    builtIn.Name,
    builtIn.NormalizedName,
    "Platform administrators",
    "  Full access.  ",
    nowUtc);

Check(
    "a built-in role still accepts display-name and description edits",
    builtIn.DisplayName == "Platform administrators"
    && builtIn.Description == "Full access.");

var custom = new Role(
    Guid.NewGuid(),
    "menu-editor",
    AuthNormalizer.NormalizeUsername("menu-editor"),
    "Menu editor",
    "   ",
    false,
    nowUtc);

Check(
    "a blank description is stored as null",
    custom.Description is null);

Check(
    "a custom role's permission set is not seeder-managed",
    !PermissionSeeder.IsPermissionSetManaged(custom));

custom.UpdateDetails(
    "menu-admin",
    AuthNormalizer.NormalizeUsername("menu-admin"),
    "Menu admin",
    "Manages menus.",
    nowUtc);

Check(
    "a custom role can be renamed",
    custom.Name == "menu-admin"
    && custom.NormalizedName == "MENU-ADMIN");

// ---------------------------------------------------------------------------
// Role assignment and auth-version invalidation.
// ---------------------------------------------------------------------------

var roleA = Guid.NewGuid();
var roleB = Guid.NewGuid();

var user = new User(
    Guid.NewGuid(),
    [roleA],
    "operator",
    AuthNormalizer.NormalizeUsername("operator"),
    "hash",
    nowUtc,
    false);

Check(
    "a new user starts at auth version 1 with its seeded roles",
    user.AuthVersion == 1
    && user.UserRoles.Count == 1);

var initialVersion = user.AuthVersion;

Check(
    "re-assigning the same role set is a no-op",
    !user.ReplaceRoles([roleA], nowUtc)
    && user.AuthVersion == initialVersion);

Check(
    "adding a role bumps the auth version",
    user.ReplaceRoles([roleA, roleB], nowUtc)
    && user.AuthVersion == initialVersion + 1
    && user.UserRoles.Count == 2);

Check(
    "removing a role bumps the auth version",
    user.ReplaceRoles([roleB], nowUtc)
    && user.AuthVersion == initialVersion + 2
    && user.UserRoles.Count == 1);

Check(
    "clearing every role is allowed and bumps the auth version",
    user.ReplaceRoles([], nowUtc)
    && user.AuthVersion == initialVersion + 3
    && user.UserRoles.Count == 0);

var beforeOutOfBand = user.AuthVersion;
user.InvalidateSessions(nowUtc);

Check(
    "an out-of-band permission change can invalidate sessions on its own",
    user.AuthVersion == beforeOutOfBand + 1);

Check(
    "an empty role id is rejected",
    Throws<ArgumentException>(
        () => user.ReplaceRoles([Guid.Empty], nowUtc)));

Check(
    "a duplicated role id collapses to a single assignment",
    new User(
        Guid.NewGuid(),
        [roleA, roleA],
        "dup",
        "DUP",
        "hash",
        nowUtc,
        false).UserRoles.Count == 1);

// ---------------------------------------------------------------------------
// Access-token claim wiring.
//
// The API gates every permission-protected endpoint on the literal claim
// values produced here, so this asserts the serialized payload decodes to
// exactly those values. A casing change on either side breaks authorization
// silently, which is what this section is here to catch.
// ---------------------------------------------------------------------------

var claims = DecodeClaims(
    roles: ["administrator", "menu-editor"],
    permissions: [AuthPermissions.UsersRead, "menu.read"],
    authVersion: 42,
    mustChangePassword: false);

var principal = new ClaimsPrincipal(
    new ClaimsIdentity(
        claims,
        "Bearer",
        JwtRegisteredClaimNames.UniqueName,
        AuthClaimNames.Role));

Check(
    "a role array decodes into one role claim per role",
    principal.FindAll(AuthClaimNames.Role)
        .Select(claim => claim.Value)
        .OrderBy(value => value, StringComparer.Ordinal)
        .SequenceEqual(
            ["administrator", "menu-editor"],
            StringComparer.Ordinal));

Check(
    "role claims drive IsInRole",
    principal.IsInRole("menu-editor"));

Check(
    "a granted permission satisfies an ordinal claim check",
    principal.HasClaim(
        AuthClaimNames.Permission,
        AuthPermissions.UsersRead));

Check(
    "an unlisted permission does not",
    !principal.HasClaim(
        AuthClaimNames.Permission,
        AuthPermissions.RolesDelete));

Check(
    "a case-variant permission does not satisfy the claim check",
    !principal.HasClaim(
        AuthClaimNames.Permission,
        AuthPermissions.UsersRead.ToUpperInvariant()));

// PermissionAuthorizationPolicyProvider requires this claim to equal
// bool.FalseString.ToLowerInvariant(); .NET's bool.ToString() would produce
// "False", so the decoded value has to be lower case.
Check(
    "must_change_password decodes to the lower-case literal the policy compares",
    principal.HasClaim(
        AuthClaimNames.MustChangePassword,
        bool.FalseString.ToLowerInvariant()));

var pendingClaims = DecodeClaims(
    roles: ["administrator"],
    permissions: [],
    authVersion: 1,
    mustChangePassword: true);

var pending = new ClaimsPrincipal(
    new ClaimsIdentity(pendingClaims, "Bearer"));

Check(
    "a pending forced password change carries no permission claims",
    !pending.FindAll(AuthClaimNames.Permission).Any());

Check(
    "a pending forced password change fails the must_change_password gate",
    !pending.HasClaim(
        AuthClaimNames.MustChangePassword,
        bool.FalseString.ToLowerInvariant())
    && pending.HasClaim(
        AuthClaimNames.MustChangePassword,
        bool.TrueString.ToLowerInvariant()));

// AuthModule re-reads this claim on every request to compare it against the
// stored auth version, so it has to survive the round trip as a plain integer.
Check(
    "auth_version decodes to an invariant integer literal",
    principal.FindFirst(AuthClaimNames.AuthVersion)?.Value == "42");

Console.WriteLine();

if (failures.Count > 0)
{
    Console.Error.WriteLine(
        $"{failures.Count} auth validation check(s) failed:");

    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    return 1;
}

Console.WriteLine("All auth role and permission validations passed.");
return 0;

static IEnumerable<Claim> DecodeClaims(
    IReadOnlyCollection<string> roles,
    IReadOnlyCollection<string> permissions,
    long authVersion,
    bool mustChangePassword)
{
    // Mirrors the payload JwtAccessTokenService writes.
    var json = JsonSerializer.Serialize(
        new TokenPayload(
            Guid.NewGuid().ToString(),
            roles,
            permissions,
            authVersion,
            mustChangePassword));

    return new JwtSecurityToken(
            new JwtHeader(),
            JwtPayload.Deserialize(json))
        .Claims;
}

static bool Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
}

internal sealed record TokenPayload(
    [property: JsonPropertyName("sub")]
    string Subject,

    [property: JsonPropertyName(AuthClaimNames.Role)]
    IReadOnlyCollection<string> Roles,

    [property: JsonPropertyName(AuthClaimNames.Permission)]
    IReadOnlyCollection<string> Permissions,

    [property: JsonPropertyName(AuthClaimNames.AuthVersion)]
    long AuthVersion,

    [property: JsonPropertyName(AuthClaimNames.MustChangePassword)]
    bool MustChangePassword);
