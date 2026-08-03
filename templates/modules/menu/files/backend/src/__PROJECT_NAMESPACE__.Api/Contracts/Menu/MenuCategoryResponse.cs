namespace __PROJECT_NAMESPACE__.Api.Contracts.Menu;

public sealed record MenuCategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    int SortOrder,
    bool IsVisible,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);