namespace __PROJECT_NAMESPACE__.Api.Contracts.Menu;

public sealed record MenuItemResponse(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Description,
    decimal Price,
    int SortOrder,
    bool IsVisible,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);