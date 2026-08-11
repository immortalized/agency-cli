namespace __PROJECT_NAMESPACE__.Operations;

public sealed record UnsealMaterialDiagnostic(
    bool IsUsable,
    string Message);

public sealed record AuthProvisioningDiagnostic(
    bool IsComplete,
    string Message);

public sealed record OpenBaoSealStatus(
    bool Initialized,
    bool Sealed,
    int Shares,
    int Threshold,
    int Progress,
    string SealType);

public sealed record OpenBaoInitializationResult(
    IReadOnlyList<byte[]> Shares,
    string RootToken);

public sealed record OpenBaoRekeyProgress(
    bool Complete,
    bool VerificationRequired,
    string Nonce,
    int Required,
    IReadOnlyList<byte[]> NewShares);
