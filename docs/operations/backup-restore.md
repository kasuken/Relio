# Backups and restore

Runbook for backups, restores and the restore drill (REL-005, issue #119). It covers both ways of
running Relio: a self-hosted instance (Docker Compose) and the hosted service (Azure). It builds on
[Data protection and encrypted fields](../security/data-protection.md) and
[Account erasure](../security/account-erasure.md).

## 1. Policy

| | Self-hosted (Docker Compose) | Hosted (Azure) |
| --- | --- | --- |
| What is backed up | The `Relio` database, and the key material in `/var/opt/relio` (data protection key ring and wrapping certificate) | Azure SQL database; key ring and certificate on App Service storage (`/home/relio`); certificate password in Key Vault |
| Schedule | Daily full backup at `RELIO_BACKUP_SCHEDULE_UTC` (default 02:30 UTC), and once when the backup container starts | Azure SQL: full weekly, differential daily, log about every 5 to 10 minutes; App Service automatic backup hourly |
| Retention | Database sets 14 days, key-material sets 90 days, the newest 3 of each are never pruned | Point-in-time restore 7 days (the Basic tier maximum), weekly long-term copies 4 weeks; App Service backups 30 days |
| Encryption | Each archive is encrypted with `age` (X25519 public key) outside SQL Server, so the server can write backups but never read them | Azure SQL backups are encrypted by transparent data encryption (service-managed key); App Service backups are managed by Azure |
| Where | Two host directories, `./backups/database` and `./backups/keys` (`RELIO_BACKUP_DIR`, `RELIO_KEY_BACKUP_DIR`), plus any off-site copy you add | Azure-managed |
| Failure signal | The backup container turns `unhealthy`, `relio-backup status` exits non-zero, optional `RELIO_BACKUP_SUCCESS_URL`/`RELIO_BACKUP_FAILURE_URL` pings | A failing run of `hosted-backup-check.yml` (optional, see section 6) |
| RPO / RTO target | 24 hours / under 1 hour for a small instance | About 10 minutes / a few hours |
| Evidence | The `restore-drill` workflow (section 7) | The same drill, plus the manual hosted drill (section 5) |

The failure signals are not application log events: the shell sidecar does not emit the reserved
`RelioBackupFailure` event (9005) described in [monitoring.md](monitoring.md). Wire the container
health, the ping URLs and the `hosted-backup-check.yml` run into the same alert channel.

Why `age` and not SQL Server backup encryption: encrypted and compressed native backups need an
edition that the Express and Web editions do not have. Encrypting outside SQL Server works on every
edition, uses a public key (so the private key never has to sit on the server) and authenticates the
file.

## 2. What you need to read a restore

All of these, together. Losing any one makes the data unreadable.

1. A database set (`relio-db-<id>`).
2. A key-material set (`relio-keys-<id>`), taken at or after the database set. The key ring only
   grows, so the newest key set reads every older database set.
3. The age **identity** (private key) the backups were encrypted to.
4. `RELIO_DP_PASSWORD`, the password of the wrapping certificate. It is never part of a backup.

Hosted: the point-in-time (or long-term) database copy, an App Service backup, and the Key Vault
secret `dp-cert-password`.

Rules:

- Keep the age identity and `RELIO_DP_PASSWORD` offline, in separate approved secret storage, never
  on the backup host. The backup host only needs the public key.
- Never delete key ring entries or previous certificates by hand. Copy a certificate you rotated out
  to `/var/opt/relio/previous/` so the key sets contain it.
- Test the identity: the restore drill decrypts with it, and so should you after generating it.

## 3. Self-hosted setup

1. On another machine (not the server) generate a key pair:

   ```bash
   docker run --rm --entrypoint age-keygen ghcr.io/kasuken/relio-backup:latest
   ```

   Store the whole output offline. Only the `age1...` public key goes on the server.
2. In `.env` set `RELIO_BACKUP_AGE_RECIPIENTS=age1...` (see `.env.example` for every setting).
3. Run `docker compose --profile backup up -d`.
4. Check it: `docker compose ps` (the `relio-backup` health), and
   `docker compose exec relio-backup relio-backup status`.
5. Take a backup now: `docker compose exec relio-backup relio-backup once`. Do this before every
   upgrade.
6. Copy `./backups` off the host. The archives are already encrypted, so any sync tool works. Do not
   keep off-site copies longer than the retention in section 1: a deleted account stays in every
   backup that holds it (section 8).
7. Choose the recovery model. A database created by Relio uses the `FULL` recovery model on
   Developer and Standard editions. Daily full backups without log backups let the log grow, and the
   backup job warns about it. For a daily RPO run
   `ALTER DATABASE [Relio] SET RECOVERY SIMPLE` once, or add your own log backups.

Each backup writes two sets, each with a `manifest.json` (ids, sizes, SHA-256 of the archive, the
schema version, the public recipients; no secrets):

```text
backups/database/relio-db-20261012T023000Z/   database.bak.gz.age  manifest.json
backups/keys/relio-keys-20261012T023000Z/     key-material.tar.gz.age  manifest.json
```

A set appears under its final name only when it is complete (`.partial` directories are unfinished).

## 4. Self-hosted restore

Use a new host, or stop the old stack first: the compose file uses fixed container names.

1. Copy the repository (or just `docker-compose.yml` and `.env`) and the two backup directories to
   the new host. A new `MSSQL_SA_PASSWORD` is fine. `RELIO_DP_PASSWORD` must be the same as before.
2. Start only SQL Server: `docker compose up -d sqlserver`.
3. Restore, mounting the identity read-only for this one command:

   ```bash
   docker compose --profile restore run --rm \
     -v /secure/relio-backup.agekey:/run/secrets/relio-backup-identity:ro \
     relio-restore --latest
   ```

   Use `--id <backupId>` for an older set. The command checks every archive against its manifest,
   decrypts, places the key material (`/var/opt/relio`, ownership and modes set for the app), restores
   the database with checksums and runs `DBCC CHECKDB`. It refuses to overwrite an existing database or
   a non-empty key directory without `--force`, and with `--force` it moves existing key files aside
   (never deletes them).
4. Start the application: `docker compose up -d relio` and wait for `/health/ready`. Startup
   authenticates every protected row with the restored keys, so a ready application proves the key
   material matches the data.
5. Re-apply account deletions made after the backup (section 8) before you reopen the instance.

Troubleshooting:

- The app restarts in a loop with a protected-data error: the key set or `RELIO_DP_PASSWORD` is
  wrong or missing. Restore the right key set with `--force`. Never start the application against an
  empty key directory: with `DataProtection__AutoGenerateIfMissing=true` it creates a new certificate
  and key ring and then correctly refuses the old data.
- `Operating system error 5` during backup or restore: the shared staging volume is not writable by
  SQL Server. Recreate the backup container, whose entrypoint fixes the ownership.

## 5. Hosted restore drill (manual, quarterly, real data)

This restores production personal data into a temporary environment, so it runs under production
controls, unlike the synthetic drill in section 7. The owner runs it.

1. Restore the database copy on the same server:
   `az sql db restore -g rg-relio-prod -s sql-relio-prod-edff -n relio --dest-name relio-drill-<yyyymmdd> --time <UTC>`.
2. Create a temporary web app `app-relio-drill-<yyyymmdd>` in the same App Service plan, with an
   access restriction that allows only your IP, no custom domain, `Email__Provider=None`,
   `Billing__Provider=None`, `Registration__Mode=Closed`.
3. Restore the key ring and certificate: `az webapp config snapshot restore -g rg-relio-prod -n
   <drill app> --source-name app-relio-prod-edff --time <snapshot time> --restore-content-only`.
4. Give the drill app's identity the Key Vault Secrets User role, and point its connection string at
   the drill database.
5. Request `/health/ready` through the restricted endpoint.
6. Delete the drill app, its role assignment and the drill database the same day.
7. Add a row to the evidence log (section 7).

## 6. Hosted configuration and monitoring

`infra/modules/sql.bicep` declares 7 days of point-in-time restore, weekly long-term retention
(`P4W`) and transparent data encryption. `infra/modules/key-vault.bicep` has an
`enablePurgeProtection` parameter, **off by default** because it cannot be undone; turning it on is
recommended for production and is the owner's decision. Secret versions of `dp-cert-password` are
the backup of the certificate password: redeploying with a different value creates a new version and
the app then cannot open the existing certificate (it fails closed); re-set the previous value to
recover.

Optional check: deploy `infra/github-backup-monitor-identity.bicep` (read-only, Reader on the
resource group), set the repository variables `AZURE_BACKUP_MONITOR_CLIENT_ID`, `AZURE_TENANT_ID`
and `AZURE_SUBSCRIPTION_ID`, and `hosted-backup-check.yml` verifies the retention settings, the
earliest restore point, the latest long-term copy and the newest App Service backup every day. It is
inactive until the variable exists. The first long-term copy can take up to a week to appear.

Known gap (follow-up): the hosted key ring lives on App Service storage and depends on App Service
automatic backups, which cannot be downloaded. Moving it to Blob Storage with Key Vault wrapping
makes key backups independent of the web app.

## 7. Restore drill evidence

`.github/workflows/restore-drill.yml` runs weekly, on demand (`workflow_dispatch`), and on pull
requests that touch backup, restore, encryption or migration code. It uses the real
`docker-compose.yml` and the real scripts, with synthetic data and throwaway generated keys and
passwords. `deploy/restore-drill/drill.sh all` runs the same steps on a Linux machine with Docker.

| Step | What it proves |
| --- | --- |
| `source-up`, `seed` | A running instance holds protected data in every protected column (including difficult moments), and it is ciphertext at rest |
| `backup-failure-signal` | A backup that cannot reach SQL Server exits non-zero and turns the health status red |
| `backup` | Encrypted database and key-material sets are produced, with manifests, and the canary appears in neither |
| `source-down` | The source instance and its volumes are destroyed |
| `restore` | A fresh SQL Server (different password, new volumes) is restored from the archives, the identity and the password only |
| `restore-up` | The restored application reaches `/health/ready` (every protected row authenticated) |
| `verify` | Exact plaintext of every canary field, row counts, schema version, and no row needed conversion |
| `negative-control` | An instance with a new key ring against the restored data refuses to start |

Evidence is the run page, its job summary and the `restore-drill-evidence` artifact (manifests, row
counts, timings, test results, kept 90 days). GitHub disables scheduled workflows after 60 days
without repository activity, so a quiet repository needs a manual run.

### Evidence log

Add a row after each drill (a run on `main`, or a manual hosted drill).

| Date | Mode | Run or ticket | Backup id | Ready after | Result | Operator |
| --- | --- | --- | --- | --- | --- | --- |
| _pending_ | Synthetic CI drill | First run of `restore-drill.yml` on `main` | | | | |

## 8. Erasure and backups

A deleted account remains in backups until they expire. Self-hosted: the database retention (14 days
by default) plus any off-site copy you keep. Hosted: up to about 35 days (7 days point-in-time restore
plus 4 weeks of weekly copies). Backups are never edited. After restoring into production from a point
before an erasure, delete those accounts again before reopening the instance; Relio keeps no erasure
ledger, so use your own records of erasure requests (a ledger is a follow-up). A drill environment
is destroyed after the drill.
