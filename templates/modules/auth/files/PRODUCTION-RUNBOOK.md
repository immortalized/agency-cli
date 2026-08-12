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

## Set up the `ops` command

Every operations task runs through the single `ops` script at the project
root. Make it executable once, right after generating or cloning the project:

```sh
chmod +x ./ops
./ops help
```

`./ops help` lists every environment, subsystem, and command with a one-line
description. The grammar is `./ops <environment> <subsystem> <verb> [args...]`,
where `<environment>` is `dev` or `production`, and `compose` is a raw
`docker compose` passthrough for that environment's file set. This replaces
the previous `npm run ops:...` and `npm run compose:...` scripts; the project
no longer ships a root `package.json`.

## Fresh bootstrap

`./ops production bootstrap-secret create` replaces the manual
`umask` / `mktemp` / `openssl rand` / `chmod` sequence. It writes a random
secret to a private temporary file outside the project and prints the exact
`export` line on stdout, so `eval` puts it into the current shell. A
subprocess cannot export into its parent shell, which is why the command
prints the line instead of setting the variable itself.

```sh
eval "$(./ops production bootstrap-secret create)"
./ops production bootstrap compose up -d database openbao
./ops production bootstrap auth init
./ops production database migrate
./ops production auth admin-create
./ops production auth test-policy
./ops production database verify
./ops production compose up -d --build
# Run this only after every bootstrap command above succeeds.
eval "$(./ops production bootstrap-secret delete)"
```

`bootstrap-secret create` deliberately relaxes the file to mode `0644`. Plain
Docker Compose implements a file-backed secret as a read-only bind mount and
cannot remap its owner or mode, so the one source file has to be readable by
both the PostgreSQL image's user and Operations UID 1654. This makes it
readable to other local host users who discover its unpredictable temporary
path during the short bootstrap window. Use a dedicated host, run
`bootstrap-secret delete` immediately after a successful bootstrap, and do not
replace this with ignored Compose `uid`/`gid`/`mode` fields unless deployment
moves to a secrets platform that actually enforces them.

`bootstrap-secret delete` shreds the file (falling back to `rm -f` where
`shred` is unavailable) and prints the matching `unset` line; running it under
`eval` as above clears the variable too. Both `bootstrap-secret` commands stay
available after bootstrap completes, because generating secret material for a
different environment is not itself a bootstrap-provisioning action.

## The bootstrap overlay is refused after bootstrap completes

Once `auth init` finishes, it has retired the PostgreSQL bootstrap login and
written its completion marker to `production_runtime_identity`. From that
point the Operations tool refuses every command invoked through the bootstrap
Compose overlay and names the steady-state command to use instead.

This is enforced by the Operations tool reading that real state at runtime,
not by the `ops` wrapper hiding the command, so running

```sh
docker compose -f compose.production.yaml -f compose.production.seal.yaml \
  -f compose.production.bootstrap.yaml --profile tools run --rm operations auth init
```

by hand is refused as well. A bootstrap interrupted part-way through
credential retirement is still resumable: the refusal applies only once the
whole sequence completed. Only destroying this deployment's database and
OpenBao volumes starts a genuinely new bootstrap.

`auth init` initializes/unseals according to the compiled strategy, provisions
Transit and both database static roles, creates separate runtime and migration
policies/tokens, verifies the engine, disables the PostgreSQL bootstrap login,
and revokes the initial OpenBao root token. The steady-state Compose file has
no bootstrap secret.

Immediately after first unseal, `auth init` replaces the in-memory root token
with a least-privilege 24-hour provisioning token in
`production_operations_state`. If provisioning is interrupted, rerun the same
bootstrap command before that token expires; it detects existing Transit and
database configuration, fills in missing stages, and avoids recreating token
files that were already committed. Successful completion revokes and deletes
the provisioning token. `./ops production openbao status` reports an
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
./ops production compose down
./ops production compose up -d database openbao
./ops production openbao status
./ops production openbao unseal
./ops production compose up -d api frontend
```

Passphrase prompts once; multi-passphrase confirms quorum and prompts distinct
operators sequentially; KMS never prompts and reports auto-unseal health. A
wrong passphrase fails authenticated decryption without changing the bundle or
OpenBao. If a multi-operator quorum is unavailable, answer `N` before any share
is submitted. API startup remains blocked on OpenBao's unsealed healthcheck.

## Audit commands

```sh
./ops production compose config
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

Run the offline validation projects as well. Neither needs Docker, PostgreSQL,
or OpenBao. The auth one covers the role and permission logic — catalog
discovery across installed modules, built-in role protection, role-assignment
auth-version bumping, and the exact access-token claim literals the
authorization policies compare:

```sh
dotnet run --project backend/validation/__PROJECT_NAMESPACE__.Auth.Validation/__PROJECT_NAMESPACE__.Auth.Validation.csproj
```

For generated passphrase and multi-passphrase projects, run their focused
initialization failure validation too:

```sh
dotnet run --project backend/validation/__PROJECT_NAMESPACE__.Operations.Validation/__PROJECT_NAMESPACE__.Operations.Validation.csproj
```

The passphrase variant injects a transient read-only Raft failure into the
first Transit mount request, verifies the retry succeeds, verifies permission
failures are not retried, and checks interrupted-versus-complete provisioning
status. The multi-passphrase variant additionally exercises atomic recovery
from a middle-operator staging failure, incomplete quorum diagnostics, and the
byte-level unseal/rekey request writer.
