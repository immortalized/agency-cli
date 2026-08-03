using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Menu;

public sealed class CreateMenuItemRequest
{
    public Guid CategoryId { get; init; }

    [Required]
    [MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; init; } = string.Empty;

    [Range(
        typeof(decimal),
        "0.00",
        "9999999999.99",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true)]
    public decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}