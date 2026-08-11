namespace __PROJECT_NAMESPACE__.Operations;

public interface IOpenBaoSystemClient
{
    Task<OpenBaoSealStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<OpenBaoInitializationResult> InitializeAsync(
        int shares,
        int threshold,
        bool autoSeal,
        CancellationToken cancellationToken = default);

    Task<OpenBaoSealStatus> SubmitUnsealShareAsync(
        ReadOnlyMemory<byte> share,
        bool reset = false,
        CancellationToken cancellationToken = default);

    Task<string> BeginRekeyAsync(
        int shares,
        int threshold,
        CancellationToken cancellationToken = default);

    Task<OpenBaoRekeyProgress> SubmitRekeyShareAsync(
        ReadOnlyMemory<byte> share,
        string nonce,
        CancellationToken cancellationToken = default);

    Task<bool> SubmitRekeyVerificationShareAsync(
        ReadOnlyMemory<byte> share,
        string nonce,
        CancellationToken cancellationToken = default);
}
