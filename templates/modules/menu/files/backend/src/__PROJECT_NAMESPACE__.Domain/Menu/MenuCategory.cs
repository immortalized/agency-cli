namespace __PROJECT_NAMESPACE__.Domain.Menu;

public sealed class MenuCategory
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public bool IsVisible { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private MenuCategory()
    {
    }

    public MenuCategory(string name, string slug, int sortOrder)
    {
        Id = Guid.NewGuid();
        Name = name;
        Slug = slug;
        SortOrder = sortOrder;
        IsVisible = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void Update(string name, string slug, int sortOrder, bool isVisible)
    {
        Name = name;
        Slug = slug;
        SortOrder = sortOrder;
        IsVisible = isVisible;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}