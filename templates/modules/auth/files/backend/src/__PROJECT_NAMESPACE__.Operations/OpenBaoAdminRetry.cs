using System.Net;
using System.Text.RegularExpressions;

namespace __PROJECT_NAMESPACE__.Operations;

internal static class OpenBaoAdminRetry
{
    private const int MaximumAttempts = 5;
    private const int MaximumDiagnosticLength = 4_096;

    private static readonly Regex SensitiveAssignmentPattern = new(
        """(?<name>\b(?:password|passwd|root[_-]?token|client[_-]?token|token|client[_-]?secret|secret|unseal[_-]?key|private[_-]?key|share|key)\b)(?<separator>["']?\s*[:=]\s*["']?)(?<value>[^"'\s,;}\]]+)""",
        RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex UriCredentialPattern = new(
        """(?<prefix>\b[a-z][a-z0-9+.-]*://[^/\s:@]+:)[^@/\s]+@""",
        RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex BearerTokenPattern = new(
        """(?<prefix>\bBearer\s+)[A-Za-z0-9._~+/=-]+""",
        RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    public static async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string description,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                await operation(cancellationToken);
                return;
            }
            catch (OpenBaoTransientAdminException exception)
                when (attempt < MaximumAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(
                    250 * (1 << (attempt - 1)));
                Console.Error.WriteLine(
                    $"OpenBao transiently could not {description} (attempt {attempt}/{MaximumAttempts}): {exception.Message} Retrying in {delay.TotalMilliseconds:0} ms.");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string description,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OpenBaoTransientAdminException exception)
                when (attempt < MaximumAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(
                    250 * (1 << (attempt - 1)));
                Console.Error.WriteLine(
                    $"OpenBao transiently could not {description} (attempt {attempt}/{MaximumAttempts}): {exception.Message} Retrying in {delay.TotalMilliseconds:0} ms.");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        var diagnosticBody = SanitizeResponseBody(responseBody);

        if (IsTransientStorageFailure(response.StatusCode, responseBody))
        {
            throw new OpenBaoTransientAdminException(
                $"{operation}; HTTP status {(int)response.StatusCode} indicated that Raft storage or leadership is not writable yet. Response: {diagnosticBody}");
        }

        throw new InvalidOperationException(
            $"OpenBao could not {operation}; HTTP status {(int)response.StatusCode}. Response: {diagnosticBody}");
    }

    private static bool IsTransientStorageFailure(
        HttpStatusCode statusCode,
        string responseBody)
    {
        if (statusCode is not (
                HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout))
        {
            return false;
        }

        return Contains(responseBody, "cannot write to readonly storage")
            || Contains(responseBody, "read-only storage")
            || Contains(responseBody, "leadership lost")
            || Contains(responseBody, "no active node")
            || Contains(responseBody, "not the active node")
            || Contains(responseBody, "standby node");
    }

    private static bool Contains(string value, string expected) =>
        value.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static string SanitizeResponseBody(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "<empty>";
        }

        var sanitized = UriCredentialPattern.Replace(
            responseBody,
            "${prefix}[REDACTED]@");
        sanitized = SensitiveAssignmentPattern.Replace(
            sanitized,
            "${name}${separator}[REDACTED]");
        sanitized = BearerTokenPattern.Replace(
            sanitized,
            "${prefix}[REDACTED]");
        sanitized = sanitized.Trim();

        return sanitized.Length <= MaximumDiagnosticLength
            ? sanitized
            : $"{sanitized[..MaximumDiagnosticLength]}...[truncated]";
    }
}

internal sealed class OpenBaoTransientAdminException(string message)
    : InvalidOperationException(message);
