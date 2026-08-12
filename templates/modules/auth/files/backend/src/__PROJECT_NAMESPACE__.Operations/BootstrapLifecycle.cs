namespace __PROJECT_NAMESPACE__.Operations;

/// <summary>
/// Refuses bootstrap-overlay Operations commands once bootstrap has already
/// completed for this deployment.
/// </summary>
/// <remarks>
/// This is enforced here rather than in the host-side <c>ops</c> wrapper so an
/// operator who invokes <c>docker compose ... -f
/// compose.production.bootstrap.yaml ... run operations ...</c> by hand is
/// refused too. The wrapper only decides what to show in help.
/// </remarks>
public static class BootstrapLifecycle
{
    /// <summary>
    /// Throws when the bootstrap overlay is active against an already
    /// bootstrapped deployment.
    /// </summary>
    public static void EnsureBootstrapOverlayIsPermitted(
        IReadOnlyCollection<string> args)
    {
        if (!IsProduction() || !IsBootstrapOverlayActive())
        {
            return;
        }

        var state = ReadState();

        if (!state.IsComplete)
        {
            return;
        }

        throw new InvalidOperationException(
            BuildRefusalMessage(args, state.CompletionMarkerFile));
    }

    /// <summary>
    /// A short line describing bootstrap state for <c>openbao status</c>.
    /// </summary>
    public static string DescribeState()
    {
        if (!IsProduction())
        {
            return "Not applicable outside production.";
        }

        var state = ReadState();

        if (state.IsComplete)
        {
            return
                "Complete; the PostgreSQL bootstrap login was retired and the bootstrap overlay is refused.";
        }

        return state.RetirementPending
            ? "Interrupted during bootstrap-credential retirement; rerun the bootstrap command to finish it."
            : "Not completed; the first 'auth init' must still run with the bootstrap overlay.";
    }

    /// <summary>
    /// The bootstrap Compose overlay is the only thing that supplies the
    /// PostgreSQL bootstrap password to this container and asks for the
    /// credential to be retired, so either variable identifies it.
    /// </summary>
    private static bool IsBootstrapOverlayActive() =>
        string.Equals(
            Environment.GetEnvironmentVariable(
                "RETIRE_DATABASE_BOOTSTRAP_CREDENTIAL"),
            "true",
            StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(
                "DATABASE_BOOTSTRAP_PASSWORD_FILE"));

    private static bool IsProduction() =>
        string.Equals(
            Environment.GetEnvironmentVariable(
                "OPERATIONS_ENVIRONMENT"),
            "Production",
            StringComparison.OrdinalIgnoreCase);

    private static BootstrapState ReadState()
    {
        var keyDirectory = Environment.GetEnvironmentVariable(
            "AUTH_KEY_DIRECTORY");

        if (string.IsNullOrWhiteSpace(keyDirectory))
        {
            // Fail closed: with the bootstrap overlay active and no state
            // directory there is no way to tell a fresh deployment from an
            // already bootstrapped one.
            throw new InvalidOperationException(
                "AUTH_KEY_DIRECTORY is not configured, so bootstrap completion state cannot be determined. Refusing to run a bootstrap-overlay command.");
        }

        var directory = Path.GetFullPath(keyDirectory.Trim());

        var completionMarkerFile = Path.Combine(
            directory,
            AuthProvisioningMarkers.CompletionFileName);

        var retirementPending = File.Exists(
            Path.Combine(
                directory,
                AuthProvisioningMarkers
                    .BootstrapRetirementPendingFileName));

        return new BootstrapState(
            File.Exists(completionMarkerFile),
            retirementPending,
            completionMarkerFile);
    }

    private static string BuildRefusalMessage(
        IReadOnlyCollection<string> args,
        string completionMarkerFile)
    {
        var attempted = args.Count > 0
            ? string.Join(' ', args)
            : "(no command)";

        var equivalent = Equivalent(args);

        return
            $"""
             Bootstrap has already completed for this deployment, so the bootstrap Compose overlay is refused.

             Attempted with the bootstrap overlay: {attempted}
             Completion marker: {completionMarkerFile}

             The PostgreSQL bootstrap login was retired during bootstrap and no longer exists, so rerunning
             the bootstrap path cannot succeed. Rerun the same command without the bootstrap overlay:

               {equivalent}

             Steady-state commands (all without the bootstrap overlay):
               ops production openbao unseal      Unseal after a restart.
               ops production openbao status      Report seal and provisioning state.
               ops production database migrate    Apply pending EF migrations.
               ops production auth admin-create   Create the initial administrator.

             Only destroying this deployment's database and OpenBao volumes starts a genuinely new bootstrap.
             """;
    }

    private static string Equivalent(IReadOnlyCollection<string> args) =>
        args.Count > 0
            ? $"ops production {string.Join(' ', args)}"
            : "ops production help";
}

internal sealed record BootstrapState(
    bool CompletionMarkerExists,
    bool RetirementPending,
    string CompletionMarkerFile)
{
    /// <summary>
    /// A run interrupted between writing the pending marker and deleting it
    /// still has work left, so it stays resumable through the overlay.
    /// </summary>
    public bool IsComplete =>
        CompletionMarkerExists && !RetirementPending;
}
