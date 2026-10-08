# Relio Self-Hosting Guide

Relio is designed to be fully self-hostable. You can run your private relationship memory notebook
on your own infrastructure with complete control over your data.

This guide provides instructions for deploying Relio with Docker Compose, configuration reference,
reverse proxy setup, backup and disaster recovery, security considerations (ADR-0002), and upgrades.

---

## Architecture & Requirements

A standard self-hosted Relio deployment consists of:

- **Relio Web Application**: .NET 10 Blazor Server application (runs on Linux/Windows, non-root user).
- **SQL Server Database**: Microsoft SQL Server 2022 (Linux container or external instance).
- **Persistent Storage**:
  - Database data volume (`/var/opt/mssql`).
  - External Data Protection key ring and wrapping certificate (`/var/opt/relio`).
- **Reverse Proxy / TLS Terminator**: Caddy, Nginx, or Traefik providing HTTPS and proxying WebSockets (`/_blazor`).
- **Hardware Sizing**: Minimum 1 GB RAM (2 GB recommended for SQL Server 2022) and 1 vCPU.

---

## Quickstart with Docker Compose

The fastest way to deploy Relio on a clean machine is with Docker Compose.

### 1. Download or clone

Clone the repository or download `docker-compose.yml` and `.env.example`:

```bash
git clone https://github.com/kasuken/Relio.git
cd Relio
```

### 2. Configure environment variables

Copy `.env.example` to `.env`:

```bash
cp .env.example .env
```

Edit `.env` and set secure passwords:

```env
# Strong SQL Server SA Password (must contain uppercase, lowercase, digits, symbols)
MSSQL_SA_PASSWORD=YourStrongSA_Passw0rd_123!

# Data Protection Certificate Password (used to encrypt the key-wrapping certificate)
RELIO_DP_PASSWORD=YourSecretKeyPassword_456!

# Registration mode: Open, InviteOnly, or Closed
REGISTRATION_MODE=Open

# Email delivery: None or Smtp
EMAIL_PROVIDER=None
```

### 3. Start the stack

```bash
docker compose up -d
```

Docker Compose starts SQL Server, waits for it to become healthy, and starts Relio.
On first boot:
- Database schema migrations apply automatically.
- A self-signed Data Protection certificate and key ring are provisioned in the persistent volume `/var/opt/relio`.
- Relio listens on port `8080`.

### 4. Create the First Administrator Account

1. Open `http://localhost:8080` (or your configured domain behind a reverse proxy).
2. Click **Create an account**.
3. The first registered account automatically becomes the instance **Administrator** regardless of the configured `REGISTRATION_MODE`.
4. Subsequent accounts follow the configured `REGISTRATION_MODE`.

---

## Configuration Reference

Relio is configured through environment variables (hierarchical keys use double underscores `__`) or `appsettings.json`.

### Database Configuration

| Setting | Environment Variable | Default | Description |
|---|---|---|---|
| `Database:Provider` | `Database__Provider` | `SqlServer` | Database provider (`SqlServer` for production, `InMemory` for tests/demos). |
| `ConnectionStrings:Relio` | `ConnectionStrings__Relio` | None | SQL Server connection string. Required for `SqlServer`. |
| `Database:ApplyMigrationsOnStartup` | `Database__ApplyMigrationsOnStartup` | `false` | When `true`, automatically applies pending EF Core migrations on application startup. Set to `true` in `docker-compose.yml`. |

### Email / SMTP Configuration

| Setting | Environment Variable | Default | Description |
|---|---|---|---|
| `Email:Provider` | `Email__Provider` | `None` | `None` disables outgoing email (no confirmation or reset emails sent). `Smtp` enables transactional emails. |
| `Email:Smtp:Host` | `Email__Smtp__Host` | None | SMTP server hostname (e.g. `smtp.sendgrid.net`). |
| `Email:Smtp:Port` | `Email__Smtp__Port` | `587` | SMTP server port. |
| `Email:Smtp:Username` | `Email__Smtp__Username` | None | SMTP authentication username. |
| `Email:Smtp:Password` | `Email__Smtp__Password` | None | SMTP authentication password. Never commit passwords. |
| `Email:Smtp:From` | `Email__Smtp__From` | None | Sender address (e.g. `relio@example.com`). |
| `Email:Smtp:EnableSsl` | `Email__Smtp__EnableSsl` | `true` | Whether to use SSL/TLS for SMTP connection. |

### Registration & User Administration

| Setting | Environment Variable | Default | Description |
|---|---|---|---|
| `Registration:Mode` | `Registration__Mode` | `Open` | Registration policy: `Open` (anyone can register), `InviteOnly` (requires admin invitation token), `Closed` (no registrations). Fresh installs with 0 users always permit the first registration. |
| `Registration:InvitationLifetime` | `Registration__InvitationLifetime` | `7.00:00:00` | Lifetime for admin invitation tokens (TimeSpan format `days.hh:mm:ss`). |
| `Administration:AdministratorEmail` | `Administration__AdministratorEmail` | None | Optional recovery email. At startup, if set, promotes this existing account to Administrator if it exists. |

### Data Protection & Cryptography (ADR-0002)

Relio encrypts sensitive user content (`HowWeMet`, `Details`, `Note.Text`, `Interaction.Description`, `Reminder.Title`, 2FA tokens) at the EF Core boundary using ASP.NET Core Data Protection.

| Setting | Environment Variable | Default | Description |
|---|---|---|---|
| `DataProtection:ApplicationName` | `DataProtection__ApplicationName` | `Relio` | Stable application identifier shared by all replicas using the same database. |
| `DataProtection:KeyRingPath` | `DataProtection__KeyRingPath` | None | Absolute path to persistent directory outside the repository (e.g. `/var/opt/relio/keys`). |
| `DataProtection:ProtectionMode` | `DataProtection__ProtectionMode` | `Certificate` | Key protection mechanism: `Certificate` (Linux/Docker/cross-platform) or `Dpapi` (Windows). |
| `DataProtection:Certificate:Path` | `DataProtection__Certificate__Path` | None | Path to PKCS#12 wrapping certificate (`.pfx`). |
| `DataProtection:Certificate:Password` | `DataProtection__Certificate__Password` | None | Secret password to decrypt the wrapping certificate. |
| `DataProtection:AutoGenerateIfMissing` | `DataProtection__AutoGenerateIfMissing` | `false` | When `true`, automatically creates a self-signed wrapping certificate if missing on first launch. |
| `DataProtection:PreviousCertificates` | `DataProtection__PreviousCertificates` | `[]` | Array of older certificates retained during certificate rotation. |

### Billing & Hosted Features

| Setting | Environment Variable | Default | Description |
|---|---|---|---|
| `Billing:Provider` | `Billing__Provider` | `None` | `None` disables billing UI and runs all features unconstrained. `Stripe` enables hosted Stripe subscription management. |
| `HostedFeatures:ProductMetrics:Enabled` | `HostedFeatures__ProductMetrics__Enabled` | `false` | Privacy-preserving aggregated product metrics (off by default). |
| `HostedFeatures:Policies:Enabled` | `HostedFeatures__Policies__Enabled` | `false` | Hosted legal policy pages (Terms/Privacy). Requires reviewed documents before enabling. |
| `Seo:PublicOrigin` | `Seo__PublicOrigin` | `https://localhost` | Canonical HTTPS public origin for SEO tags and sitemap (e.g. `https://relio.example.com`). |
| `Seo:IndexingEnabled` | `Seo__IndexingEnabled` | `false` | Explicit opt-in for search engine crawlers. Off by default. |

---

## Reverse Proxy & HTTPS Setup

Relio requires HTTPS in production. Run a reverse proxy in front of Relio that terminates TLS and forwards WebSocket connections.

Relio listens on plain HTTP port `8080` inside the container.

### Requirements:
1. Pass forwarded headers: `X-Forwarded-For`, `X-Forwarded-Proto`, and `Host`.
2. Support WebSockets for the Blazor Server circuit endpoint (`/_blazor`).

### Caddy Example (`Caddyfile`)

```caddyfile
relio.example.com {
    reverse_proxy localhost:8080
}
```

Caddy handles automatic HTTPS and WebSocket proxying out of the box.

### Nginx Example

```nginx
server {
    listen 443 ssl http2;
    server_name relio.example.com;

    ssl_certificate /etc/letsencrypt/live/relio.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/relio.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

---

## Backups, Retention & Privacy (ADR-0002)

Following Privacy Architecture Decision Record **ADR-0002**:

### What Must Be Backed Up
1. **SQL Server Database**: The database contains all relationship structures, logs, and encrypted ciphertext.
2. **Data Protection Key Material**: The `/var/opt/relio` volume contains the key ring XML files and the wrapping certificate `.pfx`.

> [!CAUTION]
> **Loss of Data Protection Keys is Irreversible**
> Stored private narrative fields (`HowWeMet`, `Details`, `Note.Text`, `Interaction.Description`, `Reminder.Title`) are encrypted with authenticated AES/GCM keys derived from the Data Protection key ring. If you lose your keys and certificate without a backup, **the encrypted fields cannot be decrypted or recovered by anyone, under any circumstances**. Always back up `/var/opt/relio` alongside your database.

### Protected Key Backups
- Store Data Protection key backups and their passwords in separate, encrypted, access-controlled storage from your database dumps.
- Do not check key ring XML files, certificates, or passwords into version control.

### Backup Schedule Recommendation
- **Database**: Nightly full backup, retained according to your organization's retention policy (e.g. 14–30 days).
- **Keys and Certificate**: Backed up immediately upon initial deployment or certificate rotation. Keys do not change unless rotated or new keys are minted by the runtime.

### Backup Commands (Docker)

To take a database backup:

```bash
docker exec -it relio-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "BACKUP DATABASE [Relio] TO DISK = N'/var/opt/mssql/backup_relio.bak' WITH NOFORMAT, NOINIT, SKIP, NOREWIND, NOUNLOAD, STATS = 10"

docker cp relio-sqlserver:/var/opt/mssql/backup_relio.bak ./backup_relio.bak
```

To back up the Data Protection keys:

```bash
docker run --rm -v relio_relio-data:/source -v $(pwd):/backup alpine tar czf /backup/relio_keys_backup.tar.gz -C /source .
```

### Account Erasure & Historical Backups (ADR-0002 Compliance)

When a user deletes their account in Relio (**Settings → Danger Zone → Delete Account**):
1. **Live Data Erasure**: All user-owned data (people, notes, interactions, reminders, difficult moments, and the Identity user account) is immediately and permanently deleted in the live SQL database in a single transaction.
2. **Historical Backups**: Account erasure operates on the live database; it does **not** retroactively rewrite or delete records inside pre-existing immutable historical database backups.
3. **Operator Responsibility**:
   - Establish an explicit backup expiration schedule (e.g., historical backups are deleted after 30 days). Once the backup expires and is overwritten, erased data is completely gone.
   - If you ever restore a historical backup into production, beware that deleted users may be temporarily resurrected. Re-apply any pending erasure requests or inform affected parties.

---

## Upgrades

Upgrading a self-hosted Relio Docker deployment is straightforward:

```bash
# 1. Pull the latest image
docker compose pull

# 2. Re-create and restart the containers
docker compose up -d
```

With `Database__ApplyMigrationsOnStartup=true` enabled in your `docker-compose.yml`:
- The updated Relio container connects to SQL Server.
- Any new database migrations are executed automatically and safely before traffic is accepted.
- Relio starts serving requests once migrations complete and the `/health/ready` check succeeds.
- Persistent data in `sqlserver-data` and keys in `relio-data` remain intact.
