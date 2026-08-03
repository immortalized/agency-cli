namespace __PROJECT_NAMESPACE__.Domain.Menu;

public sealed class MenuItem
{
    public Guid Id { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsVisible { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private MenuItem()
    {
    }

    public MenuItem(
        Guid categoryId,
        string name,
        string description,
        decimal price,
        int sortOrder)
    {
        Id = Guid.NewGuid();
        CategoryId = categoryId;
        Name = name;
        Description = description;
        Price = price;
        SortOrder = sortOrder;
        IsVisible = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void Update(
        Guid categoryId,
        string name,
        string description,
        decimal price,
        int sortOrder,
        bool isVisible)
    {
        CategoryId = categoryId;
        Name = name;
        Description = description;
        Price = price;
        SortOrder = sortOrder;
        IsVisible = isVisible;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}