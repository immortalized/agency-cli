using System.Text;

namespace __PROJECT_NAMESPACE__.Application.Auth;

public static class AuthNormalizer
{
    public static string NormalizeUsername(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        return username
            .Trim()
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
    }

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email
            .Trim()
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
    }
}