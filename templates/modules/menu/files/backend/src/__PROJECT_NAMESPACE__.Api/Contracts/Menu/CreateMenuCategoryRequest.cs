using System.ComponentModel.DataAnnotations;

namespace __PROJECT_NAMESPACE__.Api.Contracts.Menu;

public sealed class CreateMenuCategoryRequest
{
    [Required]
    [MaxLength(120)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [MaxLength(120)]
    [RegularExpression(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        ErrorMessage = "Slug must contain only lowercase letters, numbers, and hyphens.")]
    public string Slug { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}