using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class JwtKeyRing : IDisposable
{
    private readonly List<RsaSecurityKey>
        _validationKeys;

    private bool _disposed;

    private JwtKeyRing(
        string activeKeyId,
        List<RsaSecurityKey> validationKeys)
    {
        ActiveKeyId = activeKeyId;
        _validationKeys = validationKeys;
    }

    public string ActiveKeyId { get; }

    public IReadOnlyCollection<SecurityKey>
        ValidationKeys => _validationKeys;

    public static JwtKeyRing Load(
        string keyRingFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            keyRingFile);

        if (!Path.IsPathFullyQualified(
                keyRingFile))
        {
            throw new InvalidOperationException(
                "JWT key ring path must be absolute.");
        }

        if (!File.Exists(keyRingFile))
        {
            throw new FileNotFoundException(
                "JWT key ring file was not found.",
                keyRingFile);
        }

        JwtKeyRingDocument document;

        try
        {
            var json =
                File.ReadAllText(keyRingFile);

            document =
                JsonSerializer.Deserialize<
                    JwtKeyRingDocument>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive =
                            true
                    })
                ?? throw new InvalidOperationException(
                    "JWT key ring contains invalid JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "JWT key ring contains invalid JSON.",
                exception);
        }

        ValidateDocument(document);

        var keys =
            new List<RsaSecurityKey>();

        try
        {
            foreach (var entry in document.Keys)
            {
                var rsa =
                    RsaKeyLoader.LoadPublicKeyFromPem(
                        entry.PublicKeyPem);

                keys.Add(
                    new RsaSecurityKey(rsa)
                    {
                        KeyId = entry.KeyId
                    });
            }

            return new JwtKeyRing(
                document.ActiveKeyId,
                keys);
        }
        catch
        {
            DisposeKeys(keys);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeKeys(_validationKeys);
        _validationKeys.Clear();

        _disposed = true;

        GC.SuppressFinalize(this);
    }

    private static void ValidateDocument(
        JwtKeyRingDocument document)
    {
        if (document.Version != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported JWT key ring version '{document.Version}'.");
        }

        if (string.IsNullOrWhiteSpace(
                document.ActiveKeyId))
        {
            throw new InvalidOperationException(
                "JWT key ring has no active key id.");
        }

        if (document.Keys is null
            || document.Keys.Count == 0)
        {
            throw new InvalidOperationException(
                "JWT key ring has no validation keys.");
        }

        if (
            document.Keys
                .Select(entry => entry.KeyId)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != document.Keys.Count)
        {
            throw new InvalidOperationException(
                "JWT key ring contains duplicate key ids.");
        }

        if (!document.Keys.Any(entry =>
                entry.KeyId ==
                document.ActiveKeyId))
        {
            throw new InvalidOperationException(
                "JWT key ring active key does not exist.");
        }

        foreach (var entry in document.Keys)
        {
            if (string.IsNullOrWhiteSpace(
                    entry.KeyId))
            {
                throw new InvalidOperationException(
                    "JWT key ring contains an empty key id.");
            }

            if (string.IsNullOrWhiteSpace(
                    entry.PublicKeyPem))
            {
                throw new InvalidOperationException(
                    $"JWT key '{entry.KeyId}' has no public key.");
            }
        }
    }

    private static void DisposeKeys(
        IEnumerable<RsaSecurityKey> keys)
    {
        foreach (var key in keys)
        {
            key.Rsa?.Dispose();
        }
    }

    private sealed class JwtKeyRingDocument
    {
        public int Version { get; init; }

        public string ActiveKeyId { get; init; }
            = string.Empty;

        public IReadOnlyList<JwtKeyRingEntry>
            Keys
        {
            get;
            init;
        } = [];
    }

    private sealed class JwtKeyRingEntry
    {
        public string KeyId { get; init; }
            = string.Empty;

        public string PublicKeyPem { get; init; }
            = string.Empty;
    }
}