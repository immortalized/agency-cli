using System.Security.Cryptography;
using System.Text;
using __PROJECT_NAMESPACE__.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace __PROJECT_NAMESPACE__.Operations;

public static class OperationsProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            return args switch
            {
                ["auth", "init"] or ["keys", "init"] => await InitializeAuthAsync(),
                ["auth", "rotate"] or ["keys", "rotate"] => await RotateKeysAsync(),
                ["auth", "test-policy"] or ["keys", "verify-runtime-policy"] => await VerifyRuntimePolicyAsync(),
                ["auth", "admin-create"] or ["users", "create-initial-admin"] => await CreateInitialAdminAsync(),
                ["openbao", "status"] => await ShowOpenBaoStatusAsync(),
                ["openbao", "unseal"] => await UnsealOpenBaoAsync(),
                ["openbao", "rekey"] => await RekeyOpenBaoAsync(),
                ["database", "migrate"] => await MigrateDatabaseAsync(),
                ["database", "verify"] or ["database", "verify-boundary"] => await VerifyDatabaseBoundaryAsync(),
                ["database", "rotate-runtime"] => await RotateRuntimeDatabaseCredentialAsync(),
                ["help"] or [] => ShowHelp(),
                _ => ShowUnknownCommand(args)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> InitializeAuthAsync()
    {
        var environment = OperationsEnvironment.FromEnvironment();
        var provider = UnsealMaterialProviderFactory.Create();
        using var systemClient = new OpenBaoSystemClient();
        var initialization = await provider.InitializeIfNeededAsync(systemClient);

        string? bootstrapToken;
        var revokeBootstrapToken = false;
        if (initialization is not null)
        {
            bootstrapToken = initialization.RootToken;
            revokeBootstrapToken = environment.IsProduction;
        }
        else
        {
            var status = await systemClient.GetStatusAsync();
            if (status.Sealed)
            {
                throw new InvalidOperationException(
                    "OpenBao is initialized but sealed. Run 'openbao unseal' before 'auth init'.");
            }
            bootstrapToken = environment.IsProduction
                ? ReadProvisioningOrOperatorToken()
                : null;
        }

        Console.WriteLine("Provisioning Transit JWT signing and isolated database identities...");
        JwtKeyOperationResult result;
        try
        {
            result = await JwtKeyStore.InitializeAsync(
                GetKeyDirectory(),
                bootstrapToken,
                revokeBootstrapToken);
        }
        catch (InvalidOperationException exception)
            when (environment.IsProduction
                && initialization is null
                && File.Exists(
                    OpenBaoToolOptions.FromEnvironment(string.Empty)
                        .ProvisioningTokenFile)
                && exception.Message.Contains(
                    "HTTP status 403",
                    StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The stored resumable provisioning credential was rejected or has expired. Recover a temporary administrative token with OpenBao's authenticated root-generation workflow, remove the stale provisioning-token file, and rerun 'auth init' with the bootstrap overlay.",
                exception);
        }
        Console.WriteLine($"Auth provisioning complete. Active key id: {result.ActiveKeyId}; validation keys: {result.ValidationKeyCount}.");
        if (revokeBootstrapToken)
        {
            Console.WriteLine("The initial OpenBao root token and PostgreSQL bootstrap login were retired.");
        }
        return 0;
    }

    private static async Task<int> ShowOpenBaoStatusAsync()
    {
        var provider = UnsealMaterialProviderFactory.Create();
        using var client = new OpenBaoSystemClient();
        var status = await client.GetStatusAsync();
        Console.WriteLine($"Configured strategy: {provider.Strategy}");
        Console.WriteLine($"Initialized: {status.Initialized.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Sealed: {status.Sealed.ToString().ToLowerInvariant()}");
        Console.WriteLine($"OpenBao seal type: {status.SealType}");
        Console.WriteLine($"Shares/threshold/progress: {status.Shares}/{status.Threshold}/{status.Progress}");
        var material = provider.GetMaterialDiagnostic(status);
        Console.WriteLine($"Unseal material: {material.Message}");
        if (!status.Initialized)
        {
            Console.WriteLine("Auth provisioning: Not started; initialize OpenBao with 'auth init'.");
            return material.IsUsable ? 0 : 2;
        }

        var provisioning = JwtKeyStore.GetProvisioningDiagnostic(
            GetKeyDirectory(),
            OpenBaoToolOptions.FromEnvironment(string.Empty));
        Console.WriteLine($"Auth provisioning: {provisioning.Message}");
        return material.IsUsable && provisioning.IsComplete ? 0 : 2;
    }

    private static async Task<int> UnsealOpenBaoAsync()
    {
        using var client = new OpenBaoSystemClient();
        await UnsealMaterialProviderFactory.Create().UnsealAsync(client);
        var status = await client.GetStatusAsync();
        if (status.Sealed)
        {
            throw new InvalidOperationException("OpenBao remains sealed.");
        }
        Console.WriteLine("OpenBao is unsealed.");
        return 0;
    }

    private static async Task<int> RekeyOpenBaoAsync()
    {
        using var client = new OpenBaoSystemClient();
        var status = await client.GetStatusAsync();
        if (status.Sealed)
        {
            throw new InvalidOperationException("OpenBao must be unsealed before rekey begins.");
        }
        await UnsealMaterialProviderFactory.Create().RekeyAsync(client);
        Console.WriteLine("OpenBao native root-share rotation and encrypted bundle replacement completed.");
        return 0;
    }

    private static async Task<int> MigrateDatabaseAsync()
    {
        await using var dbContext = await OperationsDatabase.CreateDbContextAsync();
        await dbContext.Database.MigrateAsync();
        Console.WriteLine("Database migrations applied using the migration-only OpenBao identity.");
        return 0;
    }

    private static async Task<int> VerifyRuntimePolicyAsync()
    {
        await OpenBaoRuntimePolicyVerifier.VerifyAsync();
        Console.WriteLine("Runtime policy verified, including denial of migrator credential retrieval.");
        return 0;
    }

    private static async Task<int> VerifyDatabaseBoundaryAsync()
    {
        await RuntimeDatabaseBoundaryVerifier.VerifyAsync();
        Console.WriteLine("Runtime database boundary verified.");
        return 0;
    }

    private static async Task<int> RotateRuntimeDatabaseCredentialAsync()
    {
        var token = IsProduction()
            ? ReadCapabilityToken(
                OpenBaoToolOptions.FromEnvironment(string.Empty)
                    .DatabaseRotationTokenFile)
            : null;
        await OpenBaoDatabaseRotationVerifier.RotateAndVerifyAsync(token);
        Console.WriteLine("Runtime PostgreSQL credential rotated and verified. Recreate the API container.");
        return 0;
    }

    private static async Task<int> RotateKeysAsync()
    {
        var token = IsProduction()
            ? ReadCapabilityToken(
                OpenBaoToolOptions.FromEnvironment(string.Empty)
                    .JwtRotationTokenFile)
            : null;
        var result = await JwtKeyStore.RotateAsync(GetKeyDirectory(), token);
        Console.WriteLine($"JWT signing key rotated. Active key id: {result.ActiveKeyId}; retained validation keys: {result.ValidationKeyCount}.");
        return 0;
    }

    private static async Task<int> CreateInitialAdminAsync()
    {
        Console.Write("Administrator username: ");
        var username = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Administrator username is required.");
        }
        Console.Write("Administrator email (optional): ");
        var email = Console.ReadLine();
        await using var dbContext = await OperationsDatabase.CreateDbContextAsync();
        var creator = new InitialAdminCreator(dbContext, new Argon2PasswordHasher());
        var result = await creator.CreateAsync(username, email);
        Console.WriteLine($"Initial administrator created. User id: {result.UserId}; username: {result.Username}");
        Console.WriteLine($"Temporary password: {result.TemporaryPassword}");
        Console.WriteLine("Password change required: yes. Store this one-time value securely.");
        return 0;
    }

    private static string ReadOperatorToken()
    {
        var bytes = ConsoleSecretReader.ReadRequired("OpenBao operation-specific token: ");
        try
        {
            // HttpRequestHeaders accepts only strings. The UTF-8 input buffer is
            // cleared below, but .NET provides no supported way to clear the
            // immutable token string required by the HTTP API. Keep it scoped to
            // this operation and never persist or log it.
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string ReadProvisioningOrOperatorToken()
    {
        var path = OpenBaoToolOptions
            .FromEnvironment(string.Empty)
            .ProvisioningTokenFile;
        if (!File.Exists(path))
        {
            return ReadOperatorToken();
        }

        Console.WriteLine(
            "Resuming interrupted auth provisioning with the short-lived credential stored by the first run.");
        return SecretTextFileReader.Read(
            path,
            "The resumable OpenBao provisioning token file is empty. Supply a replacement administrative token after removing the empty file.");
    }

    private static string ReadCapabilityToken(string path)
    {
        // The HTTP header boundary requires an immutable string; the reader
        // minimizes that limitation to one decoded string and clears file bytes.
        return SecretTextFileReader.Read(
            path,
            $"The operation-specific token file '{path}' is empty.");
    }

    private static bool IsProduction() =>
        OperationsEnvironment.FromEnvironment().IsProduction;

    private static string GetKeyDirectory() =>
        Environment.GetEnvironmentVariable("AUTH_KEY_DIRECTORY")
        ?? throw new InvalidOperationException("AUTH_KEY_DIRECTORY is not configured.");

    private static int ShowHelp()
    {
        Console.WriteLine(
            """
            Privileged Operations console

            Canonical commands:
              auth init              Initialize/unseal OpenBao if needed, then provision auth and database identities.
              auth rotate            Rotate the Transit JWT signing key.
              auth admin-create      Create the initial administrator.
              auth test-policy       Verify the restricted runtime OpenBao policy.
              openbao status         Report configured strategy and actual seal state.
              openbao unseal         Unseal through the generated strategy provider.
              openbao rekey          Run explicit native OpenBao root-share rotation.
              database migrate       Retrieve only the migrator credential and apply EF migrations.
              database verify        Verify least-privilege runtime database access.
              database rotate-runtime Rotate the OpenBao-managed runtime credential.

            Legacy keys/users/database aliases remain accepted for existing npm scripts.
            """);
        return 0;
    }

    private static int ShowUnknownCommand(IReadOnlyCollection<string> args)
    {
        Console.Error.WriteLine($"Unknown command: '{string.Join(' ', args)}'. Run 'help' for usage.");
        return 1;
    }
}
