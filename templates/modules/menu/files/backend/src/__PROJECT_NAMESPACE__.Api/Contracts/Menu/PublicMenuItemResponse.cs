namespace __PROJECT_NAMESPACE__.Api.Contracts.Menu;

public sealed record PublicMenuItemResponse(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Description,
    decimal Price,
    int SortOrder);
