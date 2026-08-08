# Menu backend

Anonymous clients can read visible menu content from:

- `GET /api/menu/categories`
- `GET /api/menu/items`

Hidden categories and items are excluded, and public DTOs omit administrative
timestamps and visibility flags.

Administration is separated under `/api/admin/menu/categories` and
`/api/admin/menu/items`. The module owns and centrally enforces `menu.read`,
`menu.create`, `menu.update`, and `menu.delete`. Its permission provider is
discovered during application startup; deterministic seeding grants these
permissions to Administrator but not User.
