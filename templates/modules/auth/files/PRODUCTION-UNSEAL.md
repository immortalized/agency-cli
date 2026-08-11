# Production OpenBao unseal model

This project was generated with the **`__UNSEAL_STRATEGY__`** strategy. The
choice is compiled into the Operations executable and the production seal
overlay; it is not a runtime switch. A later change is a deliberate OpenBao
seal migration/root-share rotation performed by operators. Never copy files
between strategy variants.

## Common boundary

Production uses `compose.production.yaml` plus
`compose.production.seal.yaml`. OpenBao runs in normal server mode with
persistent integrated/Raft storage. PostgreSQL and OpenBao publish no host
ports. The development bootstrap helper, its root token/unseal volume,
development HCL, `.secrets`, database password files, and test scripts are not
mounted by either production file.

`openbao status`, `openbao unseal`, and `openbao rekey` are routed through
`IUnsealMaterialProvider`. The generated source contains only the selected
implementation. Changing `AGENCY_UNSEAL_STRATEGY` cannot change it.

## Passphrase

Single-operator mode initializes OpenBao as 1-of-1. The share is encrypted
before being persisted. Bundle version 1 records Argon2id parameters (64 MiB,
3 iterations, parallelism 2), a random 128-bit salt, AES-256-GCM, a random
96-bit nonce, ciphertext, and a 128-bit authentication tag. The format binds
authenticated associated data to `agency-openbao-unseal-bundle-v1`.

The passphrase is collected twice through hidden terminal input. It is never
accepted through argv, an option, stdin redirection, environment variables, a
file, or logs. Share/passphrase/derived-key byte buffers are cleared on a
best-effort basis after use. This protects unseal material in stolen disks,
volumes, and backups and avoids plaintext secret-file, shell-history, argv,
and environment leakage. It does **not** protect an active root-compromised
host, a keylogger/compromised operator endpoint, weak operator passphrases, or
provide multi-party compromise resistance. It is not KMS-grade protection.

Its passphrase confirmation and encrypted-storage preflight occur before
native initialization. After initialization, the one share uses the same
non-cancellable encrypted staging/retry rule as multi-passphrase, so an
ordinary bundle write failure cannot escape while the only plaintext copy is
still solely in memory.

## Multi-passphrase

This project uses **__UNSEAL_KEY_THRESHOLD__-of-__UNSEAL_KEY_SHARES__**. Each
OpenBao share is stored in its own version-1 bundle with an independent random
salt, derived key, nonce, ciphertext, tag, and operator passphrase. A
passphrase that decrypts one valid bundle cannot decrypt another. Unseal first
requires confirmation that a quorum is present, then accepts distinct share
numbers and clears each plaintext share before asking the next operator.

During first initialization, all operator passphrases are confirmed—with up
to three mismatch retries—and encrypted-storage writes are preflighted before
OpenBao is touched. After native initialization, every real share is written
to encrypted `.init-staging` storage with non-cancellable retry before any
share is submitted for unseal. A complete staged set is safely promoted on a
retry; an initialized store with missing bundles is reported as unrecoverable
instead of suggesting an unseal that cannot work. No raw share is staged.

This provides split knowledge and dual control relevant to PCI-DSS-style
requirements. It protects the same at-rest and command-channel cases as the
single-passphrase strategy and resists compromise of fewer than the threshold
operators. It does **not** protect an active root-compromised host while quorum
unseal/rekey is occurring, compromised endpoints/keyloggers for a quorum, or a
malicious quorum. It is not KMS/auto-unseal-grade protection.

`openbao rekey` uses OpenBao's native authenticated `sys/rotate/root` flow,
decrypts the current quorum sequentially, and captures every returned share
under a newly confirmed per-operator passphrase. Rekey is explicit and is
never triggered at startup.

## AWS KMS

The generated production HCL contains `seal "awskms" {}`. The seal key is
supplied as `VAULT_AWSKMS_SEAL_KEY_ID`; region and authentication use the
official AWS credential chain. Prefer an instance/task identity, so no static
AWS access key exists on the host. The Compose overlay also supports temporary
AWS environment credentials and `AWS_KMS_ENDPOINT` for a compatible test
endpoint. OpenBao 2.6 contains this provider; upgrading to 2.7+ requires the
official external KMS plugin described by OpenBao.

KMS initialization requests no local recovery shares and never generates
passphrase bundles. `openbao unseal` reports actual status and never prompts;
successful AWS KMS access auto-unseals after restart. This has the strongest
of these three guarantees because the root of trust is off-host and approaches
native KMS/auto-unseal-grade protection. It protects stolen host disks,
volumes, and backups without the KMS authority. It does not protect an active
root compromise that can use the live OpenBao/KMS identity, a compromised KMS
account/policy, unavailable KMS, or leaked static AWS credentials. Environment
credentials are visible to a sufficiently privileged host user; instance/task
identity is preferred.

## Persistent secret inventory

OpenBao owns the Transit JWT private key, database engine configuration,
runtime database password, migrator database password, and policies. Shamir
strategies additionally retain only encrypted share bundle(s). KMS retains no
local share bundle; its wrapped root key is in OpenBao storage and its root of
trust is AWS KMS.

Two restricted non-database artifacts remain outside OpenBao:

- `production_runtime_identity` holds the API's least-privilege OpenBao token
  and public JWT key ring. Only API and Operations mount it. Rotate/revoke the
  token through OpenBao when host or volume access is suspected.
- `production_operations_state` holds separate migration-only, JWT-rotation,
  and runtime-database-rotation OpenBao tokens and, for Shamir strategies,
  encrypted bundles. The API never mounts it. Each token is scoped to one
  operation; rotate/revoke it through an explicit privileged operator
  workflow before its configured maximum TTL.

Neither volume contains a root token, plaintext unseal share, database
password, JWT private key, or operator passphrase.

During an incomplete first bootstrap only, `production_operations_state` may
also contain `.openbao-provisioning-token`: a least-privilege orphan token with
a 24-hour maximum TTL used to resume Transit/database/policy provisioning after
an interruption. Successful `auth init` revokes and deletes it. It is not a
root token and cannot read arbitrary secrets, but it is privileged setup
material; protect the volume and resume promptly.
