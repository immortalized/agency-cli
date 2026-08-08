# OpenBao development security

The auth module adds an internal, single-node OpenBao 2.6 development server.
It uses HTTP and persistent integrated/Raft storage. It must not be used as a
production OpenBao configuration.

Two named Docker volumes are development-only:

- `openbao_data` contains the encrypted OpenBao storage, including Transit
  keys, policies, and Database Secrets Engine state.
- `openbao_dev_bootstrap` contains the one-share Shamir unseal key and initial
  root token. Only the continuously running development bootstrap helper and
  operator-only auth tool mount this volume. The API does not mount it.

The helper initializes a new empty store once and automatically unseals the
single node. On later starts (including an OpenBao-only container restart), it
reuses the saved unseal material. This convenience mechanism is intentionally
development-only; production must supply a deployment-specific seal and
operator bootstrap lifecycle.

OpenBao provides two capabilities to the API:

- Transit signs RS256 JWTs without exposing the private signing key.
- The Database Secrets Engine returns the current password for the project's
  least-privilege PostgreSQL runtime role.

The API receives only `.secrets/openbao-api-token`. Its policy permits Transit
signing with one key and reading one database static role. It cannot administer
OpenBao, rotate or export JWT keys, configure the database engine, rotate
database credentials, or read other database roles.

## PostgreSQL identities

The generated development stack keeps four identities separate:

- `<project>_bootstrap_admin` initializes PostgreSQL and performs controlled
  role grants. Its password is in `.secrets/database-bootstrap-password` and is
  mounted only into PostgreSQL and the privileged auth tool.
- `<project>_migrator` owns the application database and `public` schema so EF
  Core can apply migrations. Its password is in
  `.secrets/database-migrator-password`; it is available to operator tooling,
  never to an auth-enabled API container.
- `<project>_openbao_manager` has `CREATEROLE` solely to manage the runtime
  login. The auth tool creates a temporary bootstrap password in memory,
  configures OpenBao with it, and immediately asks OpenBao to rotate it. It is
  never written to the project or supplied to the API.
- `<project>_runtime` is the API database login. It has database connect,
  schema usage, application table CRUD, and sequence usage. It has no
  `SUPERUSER`, `CREATEDB`, `CREATEROLE`, schema ownership, or migration
  privileges. OpenBao owns and rotates its password; no runtime database
  password file exists.

Installing auth on an existing database project migrates away from the simple
`<project>_app` login. Application objects are transferred to the migrator and
the old login is revoked, changed to `NOLOGIN`, and has its password cleared.
PostgreSQL may require that original Docker bootstrap role to remain as the
owner of internal system objects, so it is retained only as a disabled,
non-application identity when it cannot be dropped safely.

The default static-role rotation period is 24 hours for development. Change
`OPENBAO_DATABASE_ROTATION_PERIOD` on `auth-tool` before bootstrap to configure
a different deployment-facing period.

## First-time setup

```bash
docker compose up -d database openbao
npm run auth:init
agency database update
npm run auth:admin:create
npm run auth:test-policy
npm run auth:database:verify
docker compose up -d --build api
```

`auth:init` creates the Transit key, Database Secrets Engine configuration,
PostgreSQL runtime role, restricted API policy, runtime token, and public JWT
validation ring. Run it before migrations so PostgreSQL default privileges
grant runtime access to tables and sequences subsequently created by the
migrator.

Initialization is idempotent. If the Transit key, runtime token, policy,
Database Secrets Engine configuration, and local artifacts already exist, it
verifies the restricted identity and refreshes only the public key ring. It
does not rotate or recreate the Transit key or database state. Use the specific
rotation commands when rotation is intended.

If both local runtime artifacts were deleted while OpenBao storage remains,
`auth:init` verifies the existing database engine, rewrites the same restricted
policy, and issues a replacement restricted token/key ring. It does not
reconfigure PostgreSQL or rotate OpenBao-managed database credentials.

For an ordinary restart, no auth initialization or API recreation is needed:

```bash
docker compose down
docker compose up -d
```

PostgreSQL and OpenBao named volumes, the Transit key, database-engine state,
runtime token, and public key ring are reused. The API retrieves the current
OpenBao-managed database password when its new container starts.

To deliberately erase all development database/OpenBao state:

```bash
docker compose down -v
docker compose up -d database openbao openbao-bootstrap
npm run auth:init
```

The next start initializes new OpenBao storage and new development bootstrap
material, then `auth:init` performs a fresh Agency bootstrap. The generated
`.secrets` directory can contain stale public/runtime artifacts until that
fresh `auth:init` replaces them; never copy those artifacts to production.

## Verify login and JWT behavior

Create an administrator with `npm run auth:admin:create`, start the API, and
use the printed credentials with the login endpoint:

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"identifier":"admin","password":"temporary-password"}' \
  http://localhost:8080/api/auth/login
```

The returned access token is RS256-signed, contains a versioned `kid`, and is
validated locally by ASP.NET Core with public keys from
`.secrets/auth-jwt-key-ring.json`.

## JWT key rotation

```bash
npm run auth:rotate
docker compose up -d --force-recreate api
```

Rotation creates a new Transit key version and makes its versioned `kid`
active. Previous public versions remain in the validation ring, so unexpired
tokens issued before rotation remain valid.

## Database password rotation

OpenBao rotates the static role automatically at the configured period. You can
force and verify a rotation with:

```bash
npm run auth:database:rotate
docker compose up -d --force-recreate api
```

The API reads the database credential once during startup. Npgsql pooled
connections have a five-minute maximum lifetime and one-minute idle lifetime,
but the application intentionally does not hot-swap a pool after rotation.
Recreate the API after a manual rotation, or if new connections begin failing
after an automatic rotation. Startup fails clearly if OpenBao credential
retrieval or PostgreSQL authentication fails.

## Security verification

```bash
npm run auth:test-policy
npm run auth:database:verify
```

The policy check confirms JWT signing and retrieval of the one runtime database
credential are allowed, while Transit administration, unrelated database-role
reads, database configuration, static-role changes, credential rotation, and
system administration are denied. The database check connects with the
OpenBao-managed runtime credential, exercises application CRUD, and confirms
schema creation and PostgreSQL role creation are denied.
