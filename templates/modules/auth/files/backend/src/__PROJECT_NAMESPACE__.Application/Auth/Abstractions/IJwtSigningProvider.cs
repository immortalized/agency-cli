using __PROJECT_NAMESPACE__.Application.Auth.Models;

namespace __PROJECT_NAMESPACE__.Application.Auth.Abstractions;

public interface IJwtSigningProvider
{
    string KeyId { get; }

    Task<JwtSignatureResult> SignAsync(
        ReadOnlyMemory<byte> signingInput,
        CancellationToken cancellationToken = default);
}
