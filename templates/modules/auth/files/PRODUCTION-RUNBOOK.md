# Local production runbook

Use these files only for a single-host production-like test. Replace the
internal HTTP listener with deployment-specific TLS if OpenBao traffic can
leave the private container network.

## Build a project variant

```text
agency create "Production Test" --db
cd production-test
agency add auth --unseal-strategy=passphrase
agency add menu

agency create "Production Test MP" --db
cd production-test-mp
agency add auth --unseal-strategy=multi-passphrase --unseal-shares=5 --unseal-threshold=3

agency create "Production Test KMS" --db
cd production-test-kms
agency add auth --unseal-strategy=kms
```

## Fresh bootstrap

Create a random bootstrap file outside the project. `mktemp` initially creates
it as mode `0600`; the final `chmod` deliberately relaxes it to `0644` for the
short bootstrap window, for the Compose-specific reason below:

```sh
umask 077
PRODUCTION_DATABASE_BOOTSTRAP_SECRET_FILE="$(mktemp)"
export PRODUCTION_DATABASE_BOOTSTRAP_SECRET_FILE
openssl rand -base64 48 > "$PRODUCTION_DATABASE_BOOTSTRAP_SECRET_FILE"
chmod 0644 "$PRODUCTION_DATABASE_BOOTSTRAP_SECRET_FILE"
docker compose -f compose.production.yaml -f compose.production.seal.yaml -f compose.production.bootstrap.yaml up -d database openbao
npm run ops:production:bootstrap -- auth init
npm run ops:production -- database migrate
npm run ops:production -- auth admin-create
npm run ops:production -- auth test-policy
npm run ops:production -- database verify
docker compose -f compose.production.yaml -f compose.production.seal.yaml up -d --build
# Run these two cleanup commands only after every bootstrap command above succeeds.
rm -f -- "$PRODUCTION_DATABASE_BOOTSTRAP_SECRET_FILE"
unset PRODUCTION_DATABASE_BOOTSTRAP_SECRET_FILE
```

Plain Docker Compose implements a file-backed secret as a read-only bind
mount and cannot remap its owner or mode. The temporary file therefore has
to be host-mode `0644` so both the PostgreSQL image's user and Operations UID
1654 can read the same source file. This makes it readable to other local
host users who discover its unpredictable temporary path during the short
bootstrap window. Use a dedicated host, delete the file immediately after a
successful bootstrap, and do not replace this with ignored Compose
`uid`/`gid`/`mode` fields unless deployment moves to a secrets platform that
actually enforces them.

`auth init` initializes/unseals according to the compiled strategy, provisions
Transit and both database static roles, creates separate runtime and migration
policies/tokens, verifies the engine, disables the PostgreSQL bootstrap login,
and revokes the initial OpenBao root token. After it succeeds, securely delete
the temporary bootstrap file, unset the variable, and never use the bootstrap
overlay again. The steady-state Compose file has no bootstrap secret.

Immediately after first unseal, `auth init` replaces the in-memory root token
with a least-privilege 24-hour provisioning token in
`production_operations_state`. If provisioning is interrupted, rerun the same
bootstrap command before that token expires; it detects existing Transit and
database configuration, fills in missing stages, and avoids recreating token
files that were already committed. Successful completion revokes and deletes
the provisioning token. `npm run ops:production -- openbao status` reports an
incomplete local provisioning marker and whether automatic resume material is
available without requiring a token at the prompt. If the resume token has
expired, follow OpenBao's authenticated root-generation recovery procedure
with the configured unseal quorum, then rerun `auth init` with that temporary
administrative token.

OpenBao 2.6 no longer supports `mlock`; its process memory can therefore be
paged by the host. Disable swap or use encrypted swap on the production host,
and include that setting in the host-hardening audit.

For KMS, set `VAULT_AWSKMS_SEAL_KEY_ID` and `AWS_REGION` first. Prefer a real
AWS test key and an instance/task identity. A compatible endpoint may be set
with `AWS_KMS_ENDPOINT`, but a mock that does not accurately implement KMS
Encrypt/Decrypt/DescribeKey is not a valid end-to-end test.

## Restart test

```sh
docker compose -f compose.production.yaml -f compose.production.seal.yaml down
docker compose -f compose.production.yaml -f compose.production.seal.yaml up -d database openbao
npm run ops:production -- openbao status
npm run ops:production -- openbao unseal
docker compose -f compose.production.yaml -f compose.production.seal.yaml up -d api frontend
```

Passphrase prompts once; multi-passphrase confirms quorum and prompts distinct
operators sequentially; KMS never prompts and reports auto-unseal health. A
wrong passphrase fails authenticated decryption without changing the bundle or
OpenBao. If a multi-operator quorum is unavailable, answer `N` before any share
is submitted. API startup remains blocked on OpenBao's unsealed healthcheck.

## Audit commands

```sh
docker compose -f compose.production.yaml -f compose.production.seal.yaml config
docker inspect production-test-api-1
find . -type f -name '*password*' -o -name '*unseal*'
```

Confirm only frontend publishes a port; API, PostgreSQL, and OpenBao have no
host ports; API mounts only `production_runtime_identity`; Operations alone
mounts `production_operations_state`; no Docker socket is mounted; containers
drop capabilities/use `no-new-privileges`; and production build contexts do
not contain `.secrets` or OpenBao development scripts.

Also verify API's runtime token receives HTTP 403 for the migrator credential,
the host has no plaintext migrator password, migrations work through
`database migrate`, login/forced password change/refresh/JWT rotation/database
rotation/menu permission tests still pass, and Transit public keys and OpenBao
database state survive the restart.

If `state-permissions` fails, inspect its diagnostic output first. It reports
numeric ownership, modes, filesystem type, mounts, and file attributes when
the image provides `lsattr`. On the host, locate the volume with
`docker volume inspect`, then run `lsattr -d`, `df -T`, and `mount` against its
mountpoint. Remove an unexpected immutable/append-only attribute only after
confirming the target is the generated project's volume. Hardened or remote
filesystems must permit UID/GID 1654 ownership changes; otherwise relocate the
Docker data root or adjust that host policy before retrying. The permissions
container is idempotent and skips ownership/mode syscalls when state is already
correct, including a partially processed non-empty volume.

For generated passphrase and multi-passphrase projects, run their focused
initialization failure validation as well:

```sh
dotnet run --project backend/validation/__PROJECT_NAMESPACE__.Operations.Validation/__PROJECT_NAMESPACE__.Operations.Validation.csproj
```

The passphrase variant injects a transient read-only Raft failure into the
first Transit mount request, verifies the retry succeeds, verifies permission
failures are not retried, and checks interrupted-versus-complete provisioning
status. The multi-passphrase variant additionally exercises atomic recovery
from a middle-operator staging failure, incomplete quorum diagnostics, and the
byte-level unseal/rekey request writer.
