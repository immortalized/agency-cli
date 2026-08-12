# Menu backend

Anonymous clients can read visible menu content from:

- `GET /api/menu/categories`
- `GET /api/menu/items`

Hidden categories and items are excluded, and public DTOs omit administrative
timestamps and visibility flags. These two endpoints are the module's only
public surface: `PublicMenuController` is `[AllowAnonymous]`, and the
application deliberately configures no authorization fallback policy, so they
stay reachable without a token.

Administration is separated under `/api/admin/menu/categories` and
`/api/admin/menu/items`. The module owns `menu.read`, `menu.create`,
`menu.update`, and `menu.delete`, and every administrative action carries a
`[HasPermission]` attribute enforced by the same permission handler the rest of
the API uses:

| Endpoint | Permission |
| --- | --- |
| `GET /api/admin/menu/categories` | `menu.read` |
| `GET /api/admin/menu/categories/{id}` | `menu.read` |
| `POST /api/admin/menu/categories` | `menu.create` |
| `PUT /api/admin/menu/categories/{id}` | `menu.update` |
| `DELETE /api/admin/menu/categories/{id}` | `menu.delete` |
| `GET /api/admin/menu/items` | `menu.read` |
| `GET /api/admin/menu/items/{id}` | `menu.read` |
| `POST /api/admin/menu/items` | `menu.create` |
| `PUT /api/admin/menu/items/{id}` | `menu.update` |
| `DELETE /api/admin/menu/items/{id}` | `menu.delete` |

A caller holding a valid token whose roles do not grant the required
permission receives `403`; an unauthenticated caller receives `401`. The check
happens in the authorization pipeline before the action runs, so a missing
permission can neither fall through to the handler nor surface as a `500`.

The module's permission provider is discovered during application startup and
contributes to the shared permission catalog, so these four keys appear in
`GET /api/admin/permissions` and can be granted to any custom role built
through `/api/admin/roles`. Nothing in the role system references `menu.*`
names; the menu module is the only place they are declared. Deterministic
seeding grants them to the built-in administrator role and not to the built-in
user role.
