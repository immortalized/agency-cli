using System.Security.Cryptography;
using System.Text;

namespace __PROJECT_NAMESPACE__.Operations;

internal static class SecretTextFileReader
{
    public static string Read(string path, string emptyMessage) =>
        DecodeAndClear(File.ReadAllBytes(path), emptyMessage);

    public static async Task<string> ReadAsync(
        string path,
        string emptyMessage,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return DecodeAndClear(bytes, emptyMessage);
    }

    private static string DecodeAndClear(byte[] bytes, string emptyMessage)
    {
        try
        {
            var secretBytes = TrimWhitespace(bytes);
            if (secretBytes.IsEmpty)
            {
                throw new InvalidOperationException(emptyMessage);
            }

            // Managed HTTP and database APIs accept these secrets only as strings.
            // Decode once and clear the source bytes below. .NET provides no
            // supported way to clear the immutable string, so callers must keep it
            // short-lived and must never persist or log it.
            var secret = Encoding.UTF8.GetString(secretBytes);
            return string.IsNullOrWhiteSpace(secret)
                ? throw new InvalidOperationException(emptyMessage)
                : secret;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static ReadOnlySpan<byte> TrimWhitespace(byte[] bytes)
    {
        var start = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
        while (start < bytes.Length && IsWhitespace(bytes[start]))
        {
            start++;
        }

        var end = bytes.Length;
        while (end > start && IsWhitespace(bytes[end - 1]))
        {
            end--;
        }

        return bytes.AsSpan(start, end - start);
    }

    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
