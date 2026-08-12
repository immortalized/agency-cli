namespace __PROJECT_NAMESPACE__.Application.Auth.Authorization;

/// <summary>
/// The permission keys contributed by every installed module, aggregated from
/// the registered <see cref="IPermissionDefinitionProvider"/> implementations.
/// The role system treats this as an open catalog and never hardcodes any
/// module-specific permission name.
/// </summary>
/// <remarks>
/// Permission names are compared ordinally everywhere: the seeder keys them
/// ordinally, and access-token permission claims are matched ordinally by
/// <c>ClaimsPrincipal.HasClaim</c>. Requested names are therefore only
/// trimmed, never case-folded, so a differently-cased name is reported as
/// unknown instead of silently resolving to a permission it does not match.
/// </remarks>
public sealed class PermissionCatalog
{
    private readonly Dictionary<string, PermissionDefinition> _byName;

    public PermissionCatalog(
        IEnumerable<IPermissionDefinitionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _byName = new Dictionary<string, PermissionDefinition>(
            StringComparer.Ordinal);

        foreach (var definition in providers
                     .SelectMany(provider => provider.GetPermissions()))
        {
            Validate(definition);

            if (!_byName.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException(
                    $"Permission '{definition.Name}' is declared more than once.");
            }
        }

        Definitions = _byName.Values
            .OrderBy(
                definition => definition.Name,
                StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<PermissionDefinition> Definitions { get; }

    public bool Contains(string name) =>
        _byName.ContainsKey(name);

    /// <summary>
    /// Trims, de-duplicates, and ordinally sorts the requested permission
    /// names without validating them.
    /// </summary>
    public static IReadOnlyList<string> Normalize(
        IEnumerable<string>? names)
    {
        if (names is null)
        {
            return [];
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns the normalized names that no installed module registers.
    /// </summary>
    public IReadOnlyList<string> FindUnregistered(
        IEnumerable<string>? names) =>
        Normalize(names)
            .Where(name => !_byName.ContainsKey(name))
            .ToArray();

    private static void Validate(PermissionDefinition definition)
    {
        if (definition is null
            || string.IsNullOrWhiteSpace(definition.Name)
            || string.IsNullOrWhiteSpace(definition.Module)
            || string.IsNullOrWhiteSpace(definition.Description))
        {
            throw new InvalidOperationException(
                "Permission definitions must contain a name, module, and description.");
        }

        if (definition.Name.Trim().Length != definition.Name.Length)
        {
            throw new InvalidOperationException(
                $"Permission '{definition.Name}' must not be padded with whitespace.");
        }
    }
}
