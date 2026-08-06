namespace __PROJECT_NAMESPACE__.Auth.Tool;

public sealed class JwtKeyRingDocument
{
    public int Version { get; init; } = 1;

    public required string ActiveKeyId { get; init; }

    public required IReadOnlyList<JwtKeyRingEntry>
        Keys
    {
        get;
        init;
    }
}

public sealed class JwtKeyRingEntry
{
    public required string KeyId { get; init; }

    public required string PublicKeyPem { get; init; }

    public required DateTimeOffset CreatedAtUtc
    {
        get;
        init;
    }

    public DateTimeOffset? RetiredAtUtc
    {
        get;
        init;
    }
}