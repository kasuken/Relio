# Relio backup and restore tooling (docker compose --profile backup / --profile restore).
#
# Based on the SQL Server image the compose file already pulls, so it reuses those layers and ships
# sqlcmd (/opt/mssql-tools18/bin/sqlcmd). It adds `age` (file encryption), curl (optional ping URLs)
# and CA certificates. See docs/operations/backup-restore.md.
FROM mcr.microsoft.com/mssql/server:2025-latest

# age is downloaded as a pinned release and verified against a pinned SHA-256 (the GitHub release
# digest, also checked when this file was written). Bump the version and both digests together.
ARG AGE_VERSION=1.3.2
ARG AGE_SHA256_AMD64=cbe24006683f8eb669266162894b9a522a1af52f2665fbc63a4bb032ed26ac10
ARG AGE_SHA256_ARM64=6b8dc4333c53a5a57c9e5834e3a48f92605d7154014cd07269ff3327db5d37f4

# Root on purpose: the sidecar reads the app's key files (uid 1654), reads and deletes SQL Server's
# native .bak files (uid 10001) and writes host bind mounts. It already holds the SA password,
# which outranks any filesystem permission. docker-compose.yml adds no-new-privileges and never
# mounts the Docker socket.
USER root
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl ca-certificates; \
    rm -rf /var/lib/apt/lists/*; \
    arch="$(dpkg --print-architecture)"; \
    case "$arch" in \
        amd64) expected="$AGE_SHA256_AMD64" ;; \
        arm64) expected="$AGE_SHA256_ARM64" ;; \
        *) echo "unsupported architecture: $arch" >&2; exit 1 ;; \
    esac; \
    curl -fsSL -o /tmp/age.tar.gz \
        "https://github.com/FiloSottile/age/releases/download/v${AGE_VERSION}/age-v${AGE_VERSION}-linux-${arch}.tar.gz"; \
    echo "${expected}  /tmp/age.tar.gz" | sha256sum -c -; \
    tar -xzf /tmp/age.tar.gz -C /usr/local/bin --strip-components=1 age/age age/age-keygen; \
    rm -f /tmp/age.tar.gz; \
    age --version

COPY lib.sh relio-backup relio-restore entrypoint.sh /usr/local/bin/
RUN chmod 0755 /usr/local/bin/lib.sh /usr/local/bin/relio-backup /usr/local/bin/relio-restore /usr/local/bin/entrypoint.sh

ENV PATH="/opt/mssql-tools18/bin:${PATH}"
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["schedule"]

# Unhealthy when the last backup failed or is older than RELIO_BACKUP_MAX_AGE_HOURS. The long start
# period covers the wait for SQL Server and the first backup.
HEALTHCHECK --interval=5m --timeout=30s --start-period=20m --retries=1 \
    CMD ["relio-backup", "status"]
