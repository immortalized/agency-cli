# Authentication and authorization backend

The auth module uses short-lived, OpenBao Transit-signed access tokens and
rotating refresh tokens stored as hashes in PostgreSQL. Access tokens are
validated locally with the generated public key ring. Refresh tokens are sent
only in the secure, HTTP-only refresh cookie.

## Registration

Registration is normal application configuration:

```json
{
  "Auth": {
    "RegistrationMode": "AdminOnly"
  }
}
```

Supported values are `AdminOnly` and `Public`; the default is `AdminOnly`.
`InviteOnly` can be added as another enum value in the future. Public
registration creates only an active `User` account with no administrative
permissions and does not create a session. The user signs in afterward through
the normal login endpoint.

## Forced password changes

Temporary-password users can log in, but their response and access token have
`mustChangePassword: true` and no effective permissions. These restricted
tokens can call only `GET /api/auth/me` and
`POST /api/auth/change-password`. Refresh preserves the restriction. A
successful password change increments the user's auth version and revokes all
refresh-token families, invalidating the old restricted credentials.

## Routes

| Route | Protection |
| --- | --- |
| `POST /api/auth/login` | anonymous |
| `POST /api/auth/register` | anonymous |
| `POST /api/auth/refresh` | anonymous |
| `POST /api/auth/logout` | anonymous |
| `GET /api/auth/me` | authenticated |
| `POST /api/auth/change-password` | authenticated |
| `GET /api/admin/users` | `users.read` |
| `GET /api/admin/users/{id}` | `users.read` |
| `POST /api/admin/users` | `users.create`, plus `users.assign-roles` if roles are supplied |
| `PUT /api/admin/users/{id}` | `users.update` |
| `PUT /api/admin/users/{id}/roles` | `users.assign-roles` |
| `POST /api/admin/users/{id}/disable` | `users.disable` |
| `POST /api/admin/users/{id}/enable` | `users.disable` |
| `POST /api/admin/users/{id}/reset-password` | `users.update` |
| `GET /api/admin/roles` | `roles.read` |
| `GET /api/admin/roles/{id}` | `roles.read` |
| `POST /api/admin/roles` | `roles.create` |
| `PUT /api/admin/roles/{id}` | `roles.update` |
| `DELETE /api/admin/roles/{id}` | `roles.delete` |
| `GET /api/admin/permissions` | `roles.read` |

`GET /api/auth/me` and `POST /api/auth/change-password` use the
`Auth.PasswordChangeAllowed` policy so a user with a pending forced password
change can still reach exactly those two endpoints and nothing else.

Administrator-created and reset passwords are returned exactly once and are
never stored in plaintext. Role assignment changes, role permission-set
changes, disablement, password changes, and password resets increment
`AuthVersion` and revoke refresh credentials. Each authenticated request
compares the access-token security state with the database, so stale tokens
stop working immediately.

## Roles

Roles live in `auth_roles` and are joined to users through the
`auth_user_roles` table, so a user can hold several roles and their effective
permissions are the union across those roles. A single-select role picker in a
UI needs no schema change to become multi-select later.

Two roles are seeded as built-in (`IsSystem`):

- `administrator` always holds every installed permission. Seeding re-grants
  the full catalog on every startup, so its permission set cannot be edited
  through the API; `isPermissionSetManaged` on the role response reports this.
- `user` is the default role for administrator-created and self-registered
  accounts. Its permission set *is* editable.

Built-in roles cannot be renamed or deleted, though their display name and
description can be changed. `DELETE /api/admin/roles/{id}` refuses with `409`
when the role is built-in, and also when any user is still assigned to it —
members are never silently reassigned, and the response reports how many users
must be moved first with `PUT /api/admin/users/{id}/roles`. A `RESTRICT`
foreign key enforces the same rule at the database level.

Changing a role's permission set bumps `AuthVersion` for every member and
revokes their refresh tokens, so access tokens minted against the old
permission set stop validating on the member's next request. The same happens
for one user when their own role assignment changes.

Choosing a user's roles requires `users.assign-roles`, deliberately separate
from `users.update`: without that split, anyone who could edit a profile could
grant themselves the administrator role. Callers also cannot change their own
role assignment.

## Permissions

The auth module owns `users.read`, `users.create`, `users.update`,
`users.disable`, `users.assign-roles`, `roles.read`, `roles.create`,
`roles.update`, and `roles.delete`. Other modules register their own
definitions through `IPermissionDefinitionProvider`, and the aggregated
`PermissionCatalog` is exactly what `GET /api/admin/permissions` returns, so a
custom admin UI can populate a role-editing form without hardcoding any
permission name. The role system itself references no module-specific key.

Permission names are matched ordinally everywhere: in the seeder, in the
access-token `permission` claim check, and when validating a role edit. A
request granting a differently cased name is rejected as unknown rather than
silently resolving to something it does not match.

Startup seeding is deterministic and idempotent. When installing a module adds
new permissions to the administrator role, seeding also bumps `AuthVersion`
for its members so their next token reflects the widened set.

The `auth admin-create` Operations command seeds from every installed module's
provider before creating the account, so the initial administrator holds the
complete installed permission set immediately — through the built-in
administrator role, not a separately hardcoded permission list.

## Offline validation

The role and permission logic has a focused validation project that needs
neither a database nor OpenBao:

```sh
dotnet run --project backend/validation/__PROJECT_NAMESPACE__.Auth.Validation/__PROJECT_NAMESPACE__.Auth.Validation.csproj
```

It covers catalog discovery across installed modules, ordinal permission
matching, built-in role protection, role-assignment auth-version bumping, and
the exact access-token claim literals the authorization policies compare.
