# Data protection for stored private fields

Relio uses ASP.NET Core Data Protection for authenticated encryption at the EF storage boundary.
Application services and domain objects continue to read and write their ordinary plaintext
properties; the database receives protected values. No plaintext mirror or content-search index is
created.

## Protected and readable data

The protected fields are:

- `Person.HowWeMet` and `Person.Details`
- `Note.Text`
- `Interaction.Description`
- `Reminder.Title`
- every `AspNetUserTokens.Value` value, including authenticator keys and recovery codes
- `UserProfile.UnsubscribeToken`

Password hashes remain Identity-managed hashes, not encrypted passwords. Person names, contact
values and comparison keys, dates, relationship/tag identifiers, owner links, and other indexed
metadata remain readable so Relio can list, sort, join, and enforce ownership efficiently. Do not
put private narrative content in a new plaintext column, log, URL, or search index.

Each protected column has a distinct, versioned Data Protection purpose. Null remains null and an
empty narrative remains empty after a round trip. Authentication/decryption failures are fatal:
Relio never falls back to showing stored bytes as plaintext.

## Configure an external key ring

Every environment must supply a stable application name, an absolute key-ring path outside the
application repository, and an explicit at-rest protection mode. There are no committed key,
certificate, password, or fallback defaults. The web host validates this configuration at startup,
tests that the provider can protect and authenticate a canary, and refuses to start if the configured
directory or protection material cannot be used.

### Windows with machine-level DPAPI

Provision a durable directory and grant read/write access only to the Relio service identity and
authorized operators. Set configuration through the hosting platform or environment, not a
committed `appsettings*.json` file:

```text
DataProtection__ApplicationName=Relio
DataProtection__KeyRingPath=C:\ProgramData\Relio\keys
DataProtection__ProtectionMode=Dpapi
```

DPAPI protects each key-ring entry for the local machine. A DPAPI-protected key ring is not portable
to another machine by copying files alone; include the machine's supported DPAPI recovery process in
disaster-recovery planning.

### Cross-platform certificate protection

Provision a PKCS#12 certificate with its private key using the organization's certificate process.
Store the file outside the repository and application image, mount it read-only, and deliver its
password through a secret store or environment injection. The key-ring directory itself must remain
durable and writable by the Relio identity:

```text
DataProtection__ApplicationName=Relio
DataProtection__KeyRingPath=/var/lib/relio/keys
DataProtection__ProtectionMode=Certificate
DataProtection__Certificate__Path=/run/secrets/relio-data-protection.pfx
DataProtection__Certificate__Password=<injected-from-a-secret-store>
```

For a certificate-wrapping-key rotation, configure the new certificate as `Certificate` and retain
the previous private certificate(s) until all key-ring entries wrapped by them have been securely
retired. Each previous certificate has the same `Path` and `Password` shape:

```text
DataProtection__PreviousCertificates__0__Path=/run/secrets/relio-data-protection-previous.pfx
DataProtection__PreviousCertificates__0__Password=<injected-from-a-secret-store>
```

Keep every key ring and certificate credential outside source control, build output, and container
images. Limit filesystem and secret-store access to the service identity and authorized operators.
All replicas that serve the same database or share authentication cookies must use the same
`ApplicationName`, key ring, and required private keys. Changing the application name creates a
different cryptographic boundary and makes existing protected values unreadable.

## Startup and legacy-data transition

The host must configure `AddRelioDataProtection` before registering `AddRelioData`, then run the
startup key-ring check and the sensitive-field backfill after applying the schema migration but
before demo seeding or serving requests. Do not enable ordinary application traffic until the
backfill reports success.

The migration's per-row `SensitiveDataProtectionVersion` defaults existing rows to the explicit
legacy version. Backfill runs in bounded tracked batches; each row's values and version are saved
atomically. It covers all protected fields and Identity tokens, recomputes the unsubscribe verifier,
and clears tracking after each batch. A committed batch survives a restart, while completed rows
are not encrypted again. Before continuing a partial run, it authenticates already-protected rows
with the configured key ring; after conversion it verifies every row and token. A failed, incomplete,
or unauthenticatable transition blocks startup. The maintenance mode treats all legacy values as
ordinary text, even if their contents resemble ciphertext; normal runtime reads never accept legacy
plaintext.

This transition must be run once before the new version serves the database. It is safe to retry
after an interrupted batch with the same durable key ring. Do not run it with a newly generated ring
after losing the original keys.

Migration `ProtectSensitiveFieldsAndAddProductMetrics` expands protected storage and marks existing
rows as legacy. Its downgrade is deliberately refused: shrinking encrypted columns or removing
their protection versions would destroy or misinterpret persisted data. Roll back an upgrade by
restoring a tested pre-upgrade database backup with its matching key material, not by migrating the
protected schema down or deploying a plaintext-reading version against it.

## Unsubscribe lookup

Unsubscribe tokens are encrypted but remain stable, so emails already sent keep working and later
emails can reuse the same token. A SHA-256 verifier of the high-entropy random token is stored and
indexed for lookup; it is not reversible and does not replace the encrypted token. Both the token
and verifier are infrastructure secrets and must be excluded from exports and other portability
formats.

## Search and query trade-offs

Data Protection uses randomized authenticated encryption. Equality, prefix, full-text, and ordering
queries over encrypted content are intentionally unsupported and must not be implemented by
querying ciphertext. The unsubscribe verifier is the narrow exception for exact bearer-token
lookup. If private-text search is added later, it requires a separate privacy and threat-model
decision; do not add a plaintext mirror or deterministic encrypted index as a shortcut.

## Backup, rotation, and recovery

Back up the durable key-ring directory and its required decryption material together, using an
encrypted, access-controlled backup system. For certificate mode, keep recoverable private-key
backups and their passwords in separate approved secret-management controls. For DPAPI mode, follow
the host's machine-level key recovery procedure. Test restoration to a controlled environment
before relying on it.

ASP.NET Core Data Protection can rotate its key material while retaining older keys for decrypting
existing values. Do not delete old key-ring entries or old certificate private keys just because a
new key has been created: persisted data and cookies may still depend on them. Certificate rotation
changes how key-ring entries are wrapped; it does not re-encrypt database fields.

If a key, certificate, password, directory, or backup is missing, inaccessible, or corrupted, stop
the rollout and restore the correct material before starting the application. Do not point Relio at
an empty key directory to make the startup check pass, and do not reset the protection version or
backfill data under a replacement key. Existing encrypted content is unrecoverable without its
original Data Protection keys. A wrong or incomplete ring is detected when Relio authenticates the
persisted values, and the application must remain unavailable until recovery is complete.
