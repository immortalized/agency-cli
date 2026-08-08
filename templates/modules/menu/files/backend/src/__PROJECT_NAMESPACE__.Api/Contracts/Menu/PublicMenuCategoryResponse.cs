namespace __PROJECT_NAMESPACE__.Api.Contracts.Menu;

public sealed record PublicMenuCategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    int SortOrder);
