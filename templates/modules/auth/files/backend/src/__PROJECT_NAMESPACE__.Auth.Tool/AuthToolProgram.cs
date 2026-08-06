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
            "Generating initial JWT signing key...");

        var result =
            await JwtKeyStore.InitializeAsync(
                keyDirectory);

        Console.WriteLine(
            "JWT key store initialized successfully.");

        Console.WriteLine(
            $"Active key id: {result.ActiveKeyId}");

        Console.WriteLine(
            $"Validation keys: {result.ValidationKeyCount}");

        Console.WriteLine(
            "Existing keys are never overwritten.");

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
              keys init      Generate the initial JWT signing key.
              keys rotate    Rotate the JWT signing key.
              help           Show this help message.
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