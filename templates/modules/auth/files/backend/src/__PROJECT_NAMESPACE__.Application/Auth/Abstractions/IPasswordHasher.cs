using __PROJECT_NAMESPACE__.Application.Auth.Models;

namespace __PROJECT_NAMESPACE__.Application.Auth.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(
        string password,
        string encodedHash);
}