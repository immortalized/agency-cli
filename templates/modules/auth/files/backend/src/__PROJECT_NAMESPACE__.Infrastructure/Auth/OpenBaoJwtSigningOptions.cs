namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class OpenBaoJwtSigningOptions
{
    public string Address { get; init; }
        = string.Empty;

    public string TransitMount { get; init; }
        = "transit";

    public string KeyName { get; init; }
        = string.Empty;

    public string TokenFile { get; init; }
        = string.Empty;

    public int RequestTimeoutSeconds { get; init; }
        = 10;
}
