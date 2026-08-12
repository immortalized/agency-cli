namespace __PROJECT_NAMESPACE__.Operations;

/// <summary>
/// File names of the provisioning state markers written into
/// <c>AUTH_KEY_DIRECTORY</c>. They are the source of truth for whether
/// bootstrap has completed, so both the writer (<see cref="JwtKeyStore"/>)
/// and the reader (<see cref="BootstrapLifecycle"/>) share these constants
/// rather than each spelling the names out.
/// </summary>
public static class AuthProvisioningMarkers
{
    /// <summary>
    /// Written after a successful <c>auth init</c>. In production the first
    /// successful run is only reachable through the bootstrap overlay, so its
    /// presence means the bootstrap sequence ran to completion.
    /// </summary>
    public const string CompletionFileName =
        ".auth-provisioning.complete";

    /// <summary>
    /// Written immediately before the PostgreSQL bootstrap login is retired
    /// and deleted once the whole run finished. While it exists a previous
    /// bootstrap was interrupted part-way through retirement and must still
    /// be resumable.
    /// </summary>
    public const string BootstrapRetirementPendingFileName =
        ".postgres-bootstrap-retirement.pending";
}
