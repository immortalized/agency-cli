using __PROJECT_NAMESPACE__.Application.Auth.Models;

namespace __PROJECT_NAMESPACE__.Application.Auth.Abstractions;

public interface IRefreshTokenService
{
    RefreshTokenResult Create();

    byte[] Hash(string plainTextToken);

    bool Verify(
        string plainTextToken,
        byte[] expectedHash);
}