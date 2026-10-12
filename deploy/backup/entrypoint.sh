#!/usr/bin/env bash
# Container entrypoint: prepares the working directories, then runs the requested command.
#   schedule (default)  run the backup loop
#   anything else       exec as given, e.g. `relio-backup once`, `age-keygen`
set -Eeuo pipefail
umask 077

# SQL Server runs as uid 10001 and writes its native backup here. A named volume is created
# root-owned, and BACKUP fails with "Operating system error 5" until it is handed over.
if [[ -d /staging ]]; then
    install -d -o "${RELIO_SQL_UID:-10001}" -g 0 -m 0770 /staging
fi
# The host bind mounts keep whatever mode and owner the operator gave them (mkdir -p never changes
# an existing directory); only the container's own state directory is locked down.
mkdir -p /backups/database /backups/keys
install -d -m 0700 /var/lib/relio-backup

if [[ "${1:-schedule}" == "schedule" ]]; then
    exec relio-backup schedule
fi
exec "$@"
