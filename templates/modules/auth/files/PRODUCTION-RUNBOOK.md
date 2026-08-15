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

Optionally, make the project directory part of `PATH` for the current shell so
the same script can be invoked without `./`:

```sh
export PATH="$PWD:$PATH"
ops help
```

This does not edit `.bashrc`, `.zshrc`, or any other shell profile. Add the
same export to a profile yourself only if you want it to persist, and use an
absolute project path there because `$PWD` changes. Both `ops ...` and
`./ops ...` remain valid; the rest of this runbook deliberately keeps `./ops`
so every command works even when this optional step is skipped.

Alternatively, install a symlink in a directory already on `PATH`:

```sh
sudo ln -s "$(pwd)/ops" /usr/local/bin/ops
ops help
```

The script resolves relative, absolute, and chained symlinks before changing
to the project directory, so its Compose files and build contexts are found
regardless of the caller's current directory.

`./ops help` lists every environment, subsystem, and command with a one-line
description. The grammar is `./ops <environment> <subsystem> <verb> [args...]`,
where `<environment>` is `dev` or `production`, and `compose` is a raw
`docker compose` passthrough for that environment's file set. This replaces
the previous `npm run ops:...` and `npm run compose:...` scripts; the project
no longer ships a root `package.json`.

## Pre-built image deployment

Production supports two image-sourcing modes with the same bootstrap and
restart commands:

- A source-present host has `backend/Dockerfile`,
  `backend/Operations.Dockerfile`, and `frontend/Dockerfile`. `./ops` builds
  where the command requires it.
- A pre-built-image host has `.images-only` (created by `images load`) or is
  missing any of those build contexts. `./ops` automatically adds
  `compose.production.images.yaml`, which removes all build declarations and
  refuses registry pulls for the three project images.

On the trusted source machine, build all three production images for the
target host's platform and save them into one Docker archive. Set Docker's
default platform in the shell before building. This is mandatory for a typical
AMD64 production host when the source machine is Apple Silicon/ARM64; without
it, the transferred images can fail on the target with `exec format error`.

```sh
export DOCKER_DEFAULT_PLATFORM=linux/amd64
./ops production images export ./production-images.tar
```

The export command intentionally does not pass `--platform` to individual
`docker build` commands. All three builds inherit the single exported
`DOCKER_DEFAULT_PLATFORM` value, and `./ops` refuses to export when that
variable is missing or is not exactly `linux/amd64`. It also inspects
all three resulting images and requires them to report `linux/amd64` before
creating the archive.

The command builds and verifies these fixed tags before saving them:

```text
__PROJECT_SLUG__-production-api:latest
__PROJECT_SLUG__-production-frontend:latest
__PROJECT_SLUG__-production-operations:latest
```

Because those names are explicit in `compose.production.yaml`, they do not
change when the source and target directories have different names. This
avoids Compose's otherwise directory/project-derived image-name mismatch.

Package only the image archive and deployment surface; application source is
intentionally absent:

```sh
tar -czf production-deployment.tar.gz \
  production-images.tar compose*.yaml caddy openbao ops \
  PRODUCTION-RUNBOOK.md PRODUCTION-UNSEAL.md
scp production-deployment.tar.gz operator@production-host:/srv/__PROJECT_SLUG__/
```

On the target host, unpack it in the intended deployment directory, then load
and verify the exact tags and platform:

```sh
cd /srv/__PROJECT_SLUG__
tar -xzf production-deployment.tar.gz
chmod +x ./ops
./ops production images load ./production-images.tar linux/amd64
./ops production images verify linux/amd64
```

`images load` creates `.images-only` only after all three tags load and report
the requested platform. Keep that marker with the deployment. Everything from
Fresh bootstrap onward is shared with a source-present deployment; no raw
`docker compose` fallback is needed.

## Fresh bootstrap (both image modes)

On a source-present host, start here. On a pre-built-image host, first complete
the one-time load procedure above, then run this exact same sequence unchanged.

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
./ops production compose up -d
# Run this only after every bootstrap command above succeeds.
eval "$(./ops production bootstrap-secret delete)"
```

For source-present deployments, the Operations invocations build from the
available contexts and the production Compose services use `pull_policy:
build`, so the final `up` also builds without a command-line flag. In
pre-built-image mode, `./ops` does not put `--build` on Operations runs, and
the image-only Compose overlay removes build declarations and changes the
three project services to `pull_policy: never`. The already-loaded fixed image
tags are therefore used without any build attempt or registry pull.

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

## Enable automatic HTTPS with Caddy

TLS is an opt-in production overlay. The base stack continues to publish
frontend directly on host port 80. `./ops production tls enable` creates the
local `.tls` marker; from then on every `./ops production ...` Compose or
Operations invocation automatically adds `compose.production.tls.yaml` after
the seal, bootstrap (when applicable), and pre-built-image overlays. The TLS
overlay removes frontend's host port and public-network attachment, then makes
Caddy the only service publishing host ports 80 and 443. Caddy reaches
`frontend:3000` only over the internal `application` network. Frontend keeps
proxying same-origin `/api` requests to `api:8080`, so no application URL or
API routing change is required.

Before enabling TLS, allow inbound TCP 80 and 443 in the host and cloud
firewalls. UDP 443 is optional for HTTP/3 and is also published by the overlay.
The hostname must resolve publicly to this VM, and no other host process may
occupy ports 80 or 443. Caddy uses port 80 for ACME validation and automatic
HTTP-to-HTTPS redirects. Its certificates, private keys, and ACME account state
live in the named `production_caddy_data` volume; `production_caddy_config`
also persists its autosaved configuration across container replacement.

### No owned domain: derive an sslip.io hostname

Replace the example with the VM's public IPv4 address. Converting dots to
dashes produces a real public hostname that sslip.io resolves back to that IP,
with no DNS account or domain purchase:

```sh
PUBLIC_IP=203.0.113.42 # replace with this VM's actual public IPv4 address
export TLS_DOMAIN="$(printf '%s' "$PUBLIC_IP" | tr '.' '-').sslip.io"
printf '%s\n' "$TLS_DOMAIN"
# 203-0-113-42.sslip.io
getent ahostsv4 "$TLS_DOMAIN"
```

`getent` must show the VM's public IP. If it is unavailable, use
`dig +short "$TLS_DOMAIN"`. Do not continue with the documentation-only
`203.0.113.42` address. For later operator sessions, create or edit the
project-root `.env` file so it contains the resolved value, for example
`TLS_DOMAIN=203-0-113-42.sslip.io`; Compose reads that file automatically.
Keep the `export` in the current shell because `tls enable` validates it before
creating the marker.

### Owned domain

Create an A record (and an AAAA record only if the VM really accepts IPv6
traffic) pointing the desired hostname at the VM, wait until public DNS returns
the correct address, then use the identical mechanism:

```sh
export TLS_DOMAIN=app.example.com
getent ahostsv4 "$TLS_DOMAIN"
```

Persist the same `TLS_DOMAIN=app.example.com` line in the project-root `.env`
for future sessions.

### Switch an existing HTTP deployment to HTTPS

Starting from a healthy, already-bootstrapped non-TLS deployment, keep
`TLS_DOMAIN` exported as above and run:

```sh
./ops production compose down
./ops production tls enable
./ops production compose config
./ops production compose up -d
./ops production compose ps
./ops production compose logs -f caddy
# After Caddy reports that the certificate was obtained, press Ctrl-C.
curl --fail --show-error --location "https://$TLS_DOMAIN/"
```

The `config` output should show no `ports` on frontend, ports 80/443 only on
Caddy, frontend attached only to `application`, and Caddy attached to
`application` plus `public`. Caddy waits for frontend's existing healthcheck;
its own healthcheck queries the loopback-only admin endpoint. A successful
browser or `curl` request confirms the full HTTPS-to-Caddy-to-frontend path and
a publicly trusted certificate. Caddy's default automatic HTTPS policy also
redirects `http://$TLS_DOMAIN` to HTTPS.

TLS may instead be enabled before Fresh bootstrap. Keep `TLS_DOMAIN` exported,
run `./ops production tls enable`, and then use the Fresh bootstrap commands
unchanged. The targeted `database openbao` start and Operations runs do not
start Caddy; the final `./ops production compose up -d` starts frontend and
Caddy after bootstrap succeeds. Pre-built-image mode composes in the same way:
the images overlay affects only api/frontend/operations, while Caddy pulls its
separately pinned image.

To return to direct HTTP, first run `./ops production compose down` while the
marker still exists, then `./ops production tls disable`, and finally
`./ops production compose up -d`. Removing the marker before `down` would hide
Caddy from that Compose file set and leave its old container behind.

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

Creating the resumable policy, its constrained token role, and the first
24-hour token necessarily happens before that token can be stored. Every
OpenBao administration request in this sequence retries transient Raft
storage/leader-election responses. If all retries are exhausted during this
small pre-storage window, a later `auth init` cannot resume automatically; it
now explains how to obtain a temporary administrative token through
authenticated root generation and that recreating this deployment's OpenBao
and database volumes is the only fresh-start option when recovery material is
unavailable.

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

With TLS enabled, keep `TLS_DOMAIN` exported (or in the project-root `.env`)
and replace the final command with:

```sh
./ops production compose up -d api frontend caddy
./ops production compose ps
curl --fail --show-error --location "https://$TLS_DOMAIN/"
```

The first `down` uses the active TLS overlay, but it does not remove named
volumes, so the Caddy certificate/ACME state survives. The final start waits on
the same API and frontend healthchecks before Caddy becomes healthy.

## Audit commands

```sh
./ops production compose config
docker inspect production-test-api-1
find . -type f -name '*password*' -o -name '*unseal*'
```

Without TLS, confirm only frontend publishes a port. With TLS enabled, confirm
only Caddy publishes ports and frontend has no host port. In both modes, API,
PostgreSQL, and OpenBao have no host ports; API mounts only
`production_runtime_identity`; Operations alone mounts
`production_operations_state`; no Docker socket is mounted; containers drop
capabilities/use `no-new-privileges`; and production build contexts do not
contain `.secrets` or OpenBao development scripts. Caddy should have only the
`NET_BIND_SERVICE` capability added, a read-only root filesystem, a hardened
temporary filesystem, and named volumes mounted at `/data` and `/config`.

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
