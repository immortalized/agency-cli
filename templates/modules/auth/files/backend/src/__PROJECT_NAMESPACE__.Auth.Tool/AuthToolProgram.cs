using __PROJECT_NAMESPACE__.Infrastructure.Auth;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class AuthToolProgram
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            return args switch
            {
                ["keys", "init"] =>
                    await InitializeKeysAsync(),

                ["keys", "rotate"] =>
                    await RotateKeysAsync(),

                ["keys", "verify-runtime-policy"] =>
                    await VerifyRuntimePolicyAsync(),

                ["users", "create-initial-admin"] =>
                    await CreateInitialAdminAsync(),

                ["help"] or [] =>
                    ShowHelp(),

                _ =>
                    ShowUnknownCommand(args)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Error: {exception.Message}");

            return 1;
        }
    }

    private static async Task<int>
        InitializeKeysAsync()
    {
        var keyDirectory =
            GetKeyDirectory();

        Console.WriteLine(
            "Bootstrapping OpenBao Transit JWT signing...");

        var result =
            await JwtKeyStore.InitializeAsync(
                keyDirectory);

        Console.WriteLine(
            "OpenBao Transit JWT signing initialized successfully.");

        Console.WriteLine(
            $"Active key id: {result.ActiveKeyId}");

        Console.WriteLine(
            $"Validation keys: {result.ValidationKeyCount}");

        Console.WriteLine(
            "Only public validation keys were written to the project.");

        return 0;
    }

    private static async Task<int>
        VerifyRuntimePolicyAsync()
    {
        Console.WriteLine(
            "Verifying the OpenBao runtime API policy...");

        await OpenBaoRuntimePolicyVerifier.VerifyAsync();

        Console.WriteLine(
            "Runtime policy verified: configured signing is allowed and privileged operations are denied.");

        return 0;
    }

    private static async Task<int>
        RotateKeysAsync()
    {
        var keyDirectory =
            GetKeyDirectory();

        Console.WriteLine(
            "Rotating JWT signing key...");

        var result =
            await JwtKeyStore.RotateAsync(
                keyDirectory);

        Console.WriteLine(
            "JWT signing key rotated successfully.");

        Console.WriteLine(
            $"New active key id: {result.ActiveKeyId}");

        Console.WriteLine(
            $"Validation keys retained: {result.ValidationKeyCount}");

        Console.WriteLine(
            "Recreate the API container before issuing tokens with the new key:");

        Console.WriteLine(
            "docker compose up -d --force-recreate api");

        return 0;
    }

    private static async Task<int>
        CreateInitialAdminAsync()
    {
        Console.Write(
            "Administrator username: ");

        var username = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                "Administrator username is required.");
        }

        Console.Write(
            "Administrator email (optional): ");

        var email = Console.ReadLine();

        await using var dbContext =
            AuthToolDatabase.CreateDbContext();

        var creator = new InitialAdminCreator(
            dbContext,
            new Argon2PasswordHasher());

        var result =
            await creator.CreateAsync(
                username,
                email);

        Console.WriteLine();
        Console.WriteLine(
            "Initial administrator created successfully.");
        Console.WriteLine();

        Console.WriteLine(
            $"User id: {result.UserId}");

        Console.WriteLine(
            $"Username: {result.Username}");

        if (result.Email is not null)
        {
            Console.WriteLine(
                $"Email: {result.Email}");
        }

        Console.WriteLine(
            $"Temporary password: {result.TemporaryPassword}");

        Console.WriteLine(
            "Password change required: yes");

        Console.WriteLine();

        Console.WriteLine(
            "Store the temporary password securely. It will not be displayed again.");

        return 0;
    }

    private static string GetKeyDirectory()
    {
        var keyDirectory =
            Environment.GetEnvironmentVariable(
                "AUTH_KEY_DIRECTORY");

        if (string.IsNullOrWhiteSpace(
                keyDirectory))
        {
            throw new InvalidOperationException(
                "AUTH_KEY_DIRECTORY is not configured.");
        }

        return keyDirectory;
    }

    private static int ShowHelp()
    {
        Console.WriteLine(
            """
            Auth Tool

            Usage:
              keys init                    Bootstrap Transit, the JWT key, policy and runtime token.
              keys rotate                  Rotate the OpenBao Transit JWT signing key.
              keys verify-runtime-policy   Exercise allowed and denied runtime API operations.
              users create-initial-admin   Create the first administrator account.
              help                         Show this help message.
            """);

        return 0;
    }

    private static int ShowUnknownCommand(
        IReadOnlyCollection<string> args)
    {
        var command = args.Count == 0
            ? string.Empty
            : string.Join(" ", args);

        Console.Error.WriteLine(
            $"Unknown command: '{command}'.");

        Console.Error.WriteLine(
            "Run the tool with 'help' to see the available commands.");

        return 1;
    }
}
