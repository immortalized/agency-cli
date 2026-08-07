namespace __PROJECT_NAMESPACE__.Application.Auth.Models;

public sealed record JwtSignatureResult(
    ReadOnlyMemory<byte> Signature);
