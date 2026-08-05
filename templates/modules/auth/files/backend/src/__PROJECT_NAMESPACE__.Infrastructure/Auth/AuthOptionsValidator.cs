using System.Text;
using Microsoft.Extensions.Options;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class AuthOptionsValidator
    : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AuthOptions options)
    {
        var errors = new List<string>();

        if (options.RefreshTokenLifetimeDays is < 1 or > 90)
        {
            errors.Add(
                "Auth:RefreshTokenLifetimeDays must be between 1 and 90.");
        }

        if (string.IsNullOrWhiteSpace(
                options.RefreshCookieName))
        {
            errors.Add(
                "Auth:RefreshCookieName must be configured.");
        }
        else if (!options.RefreshCookieName.StartsWith(
                     "__Host-",
                     StringComparison.Ordinal))
        {
            errors.Add(
                "Auth:RefreshCookieName must start with '__Host-'.");
        }

        if (string.IsNullOrWhiteSpace(
                options.BootstrapSecret))
        {
            errors.Add(
                "Auth:BootstrapSecret must be configured.");
        }
        else if (
            Encoding.UTF8.GetByteCount(
                options.BootstrapSecret) < 32)
        {
            errors.Add(
                "Auth:BootstrapSecret must contain at least 32 UTF-8 bytes.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}