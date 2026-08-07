# OpenBao JWT signing (development only)

The auth module adds an internal OpenBao 2.6 development server. It is unsealed,
uses HTTP and in-memory storage, and has a project-specific development root
token in `compose.override.yaml`. It must not be used as a production OpenBao
configuration.

The API never receives the development root token or an RSA private key. After
bootstrap it receives only `.secrets/openbao-api-token`, whose policy permits
`update` on the single project Transit signing endpoint. The only JWT key file
written to the project is `.secrets/auth-jwt-key-ring.json`, containing public
RSA keys for local validation.

## First-time setup

```bash
docker compose up -d database openbao
npm run auth:init
agency database update
npm run auth:admin:create
docker compose up -d --build api
npm run auth:test-policy
```

`auth:init` refuses to run again while the current OpenBao Transit key and both
local bootstrap artifacts already exist. Use `auth:rotate` for key rotation.
If the development OpenBao container is recreated, its in-memory key is gone;
in that recovery case `auth:init` replaces the now-stale local public ring and
runtime token.

Set the credentials printed by `auth:admin:create`, then verify login, JWT
claims, RS256 metadata and ASP.NET Core JWT Bearer validation:

```bash
AUTH_TEST_IDENTIFIER=admin \
AUTH_TEST_PASSWORD='temporary-password' \
npm run auth:test-jwt
```

The following integration check rotates the Transit key, recreates the API,
and verifies both a token issued before rotation and one issued afterward:

```bash
AUTH_TEST_IDENTIFIER=admin \
AUTH_TEST_PASSWORD='temporary-password' \
npm run auth:test-rotation
```

## Rotation

```bash
npm run auth:rotate
docker compose up -d --force-recreate api
```

Rotation creates a new OpenBao key version and makes its versioned `kid`
active. All public versions remain in the validation ring so unexpired tokens
issued before rotation continue to validate.

Because development mode stores OpenBao state only in memory, recreating the
OpenBao container loses its keys and tokens. Run `npm run auth:init` again after
that happens; it replaces the stale development public ring and runtime token
with material from the new in-memory instance, then recreate the API container.
