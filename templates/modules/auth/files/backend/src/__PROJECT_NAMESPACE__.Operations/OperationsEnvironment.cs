namespace __PROJECT_NAMESPACE__.Operations;

public sealed record OperationsEnvironment(
    Uri OpenBaoAddress,
    string EnvironmentName,
    string MaterialDirectory,
    TimeSpan RequestTimeout)
{
    public bool IsProduction => string.Equals(
        EnvironmentName,
        "Production",
        StringComparison.OrdinalIgnoreCase);

    public static OperationsEnvironment FromEnvironment()
    {
        var addressValue = Required("OPENBAO_ADDRESS");
        if (!Uri.TryCreate(addressValue, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "OPENBAO_ADDRESS must be an absolute HTTP or HTTPS address.");
        }

        var timeoutValue = Environment.GetEnvironmentVariable(
            "OPENBAO_REQUEST_TIMEOUT_SECONDS") ?? "10";
        if (!int.TryParse(timeoutValue, out var timeoutSeconds)
            || timeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException(
                "OPENBAO_REQUEST_TIMEOUT_SECONDS must be between 1 and 120.");
        }

        return new OperationsEnvironment(
            address,
            Environment.GetEnvironmentVariable("OPERATIONS_ENVIRONMENT") ?? "Development",
            Path.GetFullPath(Required("OPENBAO_UNSEAL_MATERIAL_DIRECTORY")),
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is not configured.")
            : value.Trim();
    }
}
