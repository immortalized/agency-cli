using __PROJECT_NAMESPACE__.Application.Auth.Authorization;

namespace __PROJECT_NAMESPACE__.Application.Menu;

public static class MenuPermissions
{
    public const string Read = "menu.read";
    public const string Create = "menu.create";
    public const string Update = "menu.update";
    public const string Delete = "menu.delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "menu", "Read menu administration data."),
        new(Create, "menu", "Create menu categories and items."),
        new(Update, "menu", "Update menu categories and items."),
        new(Delete, "menu", "Delete menu categories and items.")
    ];
}
