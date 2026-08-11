namespace __PROJECT_NAMESPACE__.Operations;

public interface IUnsealMaterialProvider
{
    string Strategy { get; }

    UnsealMaterialDiagnostic GetMaterialDiagnostic(
        OpenBaoSealStatus status);

    Task<OpenBaoInitializationResult?>
        InitializeIfNeededAsync(
            IOpenBaoSystemClient client,
            CancellationToken cancellationToken = default);

    Task UnsealAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default);

    Task RekeyAsync(
        IOpenBaoSystemClient client,
        CancellationToken cancellationToken = default);
}
