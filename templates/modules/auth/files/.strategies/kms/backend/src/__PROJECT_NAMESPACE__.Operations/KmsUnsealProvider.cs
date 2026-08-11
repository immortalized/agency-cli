namespace __PROJECT_NAMESPACE__.Operations;

public sealed class KmsUnsealProvider : IUnsealMaterialProvider
{
    public string Strategy => "kms";

    public UnsealMaterialDiagnostic GetMaterialDiagnostic(
        OpenBaoSealStatus status) =>
        new(
            true,
            "Native AWS KMS auto-unseal uses no local share bundles.");

    public async Task<OpenBaoInitializationResult?> InitializeIfNeededAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default)
    {
        var status = await client.GetStatusAsync(cancellationToken);
        if (status.Initialized)
        {
            return null;
        }

        var result = await client.InitializeAsync(
            shares: 0,
            threshold: 0,
            autoSeal: true,
            cancellationToken: cancellationToken);
        status = await client.GetStatusAsync(cancellationToken);
        if (status.Sealed)
        {
            throw new InvalidOperationException(
                "OpenBao was initialized with AWS KMS but did not auto-unseal. Verify the KMS key, region, endpoint, and AWS credentials.");
        }
        return result;
    }

    public async Task UnsealAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default)
    {
        var status = await client.GetStatusAsync(cancellationToken);
        Console.WriteLine(
            status.Sealed
                ? "Strategy kms uses native AWS KMS auto-unseal, but OpenBao is currently sealed. Check KMS availability and credentials; no passphrase will be requested."
                : "Strategy kms uses native AWS KMS auto-unseal. OpenBao is unsealed; no manual action is required.");
        if (status.Sealed)
        {
            throw new InvalidOperationException("AWS KMS auto-unseal is not healthy.");
        }
    }

    public Task RekeyAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "The kms strategy has no Shamir unseal shares. Rotate the AWS KMS key through the provider and use OpenBao's authenticated root/recovery rotation workflow explicitly.");
}
