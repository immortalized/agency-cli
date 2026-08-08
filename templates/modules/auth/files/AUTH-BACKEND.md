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

- `POST /api/auth/login`
- `POST /api/auth/register`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/auth/me`
- `POST /api/auth/change-password`
- `GET /api/admin/users`
- `GET /api/admin/users/{id}`
- `POST /api/admin/users`
- `PUT /api/admin/users/{id}`
- `POST /api/admin/users/{id}/disable`
- `POST /api/admin/users/{id}/enable`
- `POST /api/admin/users/{id}/reset-password`

Administrator-created and reset passwords are returned exactly once and are
never stored in plaintext. Role changes, disablement, password changes, and
password resets increment `AuthVersion` and revoke refresh credentials where
appropriate. Each authenticated request compares the access-token security
state with the database, so stale tokens stop working immediately.

## Permissions

The auth module owns `users.read`, `users.create`, `users.update`, and
`users.disable`. Other modules register their own permission definitions.
Startup seeding is deterministic and idempotent: Administrator receives all
installed administrative permissions, while User receives none.
