#!/usr/bin/env bash
# Shared helpers for relio-backup and relio-restore. Sourced, never executed.
#
# Rules this file keeps for every script that uses it:
#   - never print a password, key, identity or any database content; log ids, sizes and counts only
#   - sqlcmd reads the password from SQLCMDPASSWORD, never from a command line argument

export TZ=UTC

: "${RELIO_BACKUP_SQL_SERVER:=sqlserver,1433}"
: "${RELIO_BACKUP_SQL_USER:=sa}"
: "${RELIO_BACKUP_DATABASE:=Relio}"
: "${RELIO_BACKUP_SCHEDULE_UTC:=02:30}"
: "${RELIO_BACKUP_RETENTION_DAYS:=14}"
: "${RELIO_KEY_BACKUP_RETENTION_DAYS:=90}"
: "${RELIO_BACKUP_KEEP_MIN:=3}"
: "${RELIO_BACKUP_MAX_AGE_HOURS:=26}"
: "${RELIO_BACKUP_RUN_ON_START:=true}"
: "${RELIO_BACKUP_SUCCESS_URL:=}"
: "${RELIO_BACKUP_FAILURE_URL:=}"
: "${RELIO_BACKUP_AGE_RECIPIENTS:=}"
: "${RELIO_KEY_BACKUP_AGE_RECIPIENTS:=}"
: "${RELIO_BACKUP_FILE_OWNER:=}"
: "${RELIO_KEY_SOURCE:=/var/opt/relio}"
: "${RELIO_SQL_STAGING_PATH:=/var/opt/mssql/backup}"
: "${RELIO_LOCAL_STAGING_PATH:=/staging}"
: "${RELIO_BACKUP_DB_DIR:=/backups/database}"
: "${RELIO_BACKUP_KEY_DIR:=/backups/keys}"
: "${RELIO_BACKUP_STATE_DIR:=/var/lib/relio-backup}"
: "${RELIO_APP_UID:=1654}"
: "${RELIO_SQL_UID:=10001}"

utc_now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

backup_id() { date -u +%Y%m%dT%H%M%SZ; }

# log <level> <message...>
log() {
    local level="$1"
    shift
    printf '%s: %s %s %s\n' "${RELIO_LOG_PREFIX:-relio-backup}" "$(utc_now)" "$level" "$*" >&2
}

die() {
    log ERROR "$*"
    exit 1
}

sha256_of() { sha256sum -- "$1" | cut -d' ' -f1; }

file_size() { stat -c %s -- "$1"; }

require_uint() {
    local name="$1" value="${!1:-}"
    [[ "$value" =~ ^[0-9]+$ ]] || die "$name must be a whole number"
}

# A database name is spliced into T-SQL as [name]; only a conservative alphabet is accepted so the
# value can never break out of the bracketed identifier or the quoted file names.
validate_database_name() {
    [[ "$RELIO_BACKUP_DATABASE" =~ ^[A-Za-z0-9_]{1,100}$ ]] \
        || die "RELIO_BACKUP_DATABASE must contain only letters, digits and underscores"
}

# sql_run <query>: run a statement, failing on any SQL error. Informational output (BACKUP progress)
# is dropped; error messages (severity 11 and up) go to stderr.
sql_run() {
    sqlcmd -S "$RELIO_BACKUP_SQL_SERVER" -U "$RELIO_BACKUP_SQL_USER" -C -b -r0 -l 30 -Q "$1" >/dev/null
}

# sql_scalar <query>: print the first column of the first row, trimmed, and nothing else.
sql_scalar() {
    sqlcmd -S "$RELIO_BACKUP_SQL_SERVER" -U "$RELIO_BACKUP_SQL_USER" -C -b -r0 -l 30 -h -1 -W \
        -Q "SET NOCOUNT ON; $1" | sed -n '1{s/\r$//;p}'
}

# sql_lines <query>: print every row, columns separated by "|", trimmed.
sql_lines() {
    sqlcmd -S "$RELIO_BACKUP_SQL_SERVER" -U "$RELIO_BACKUP_SQL_USER" -C -b -r0 -l 30 -h -1 -W -s '|' \
        -Q "SET NOCOUNT ON; $1" | sed 's/\r$//'
}

# json_escape <text>: escape a value for a JSON string literal.
json_escape() {
    local s="$1"
    s="${s//\\/\\\\}"
    s="${s//\"/\\\"}"
    s="${s//$'\n'/\\n}"
    s="${s//$'\r'/\\r}"
    s="${s//$'\t'/\\t}"
    printf '%s' "$s"
}

# json_get <file> <key>: read a scalar written by this tooling. Manifests and the state file keep one
# key per line and never put a quote or comma inside a value, so a line match is enough (and jq is
# not needed in the image).
json_get() {
    sed -n "s/^[[:space:]]*\"$2\":[[:space:]]*\"\{0,1\}\([^\",]*\)\"\{0,1\},\{0,1\}[[:space:]]*\$/\1/p" "$1" | sed -n '1p'
}

# build_recipient_args <comma or space separated list>: fills RECIPIENT_ARGS with `-r key` pairs. Only
# well-formed X25519 public keys are accepted, so a mistyped list fails before anything is written.
RECIPIENT_ARGS=()
build_recipient_args() {
    local list="${1//,/ }" key
    local -a keys=()
    RECIPIENT_ARGS=()
    read -r -a keys <<<"$list"
    for key in "${keys[@]}"; do
        [[ "$key" =~ ^age1[0-9a-z]{58}$ ]] \
            || die "a backup recipient is not a valid age public key (expected age1..., 62 characters)"
        RECIPIENT_ARGS+=(-r "$key")
    done
    ((${#RECIPIENT_ARGS[@]} > 0)) || die "no backup recipients configured (set RELIO_BACKUP_AGE_RECIPIENTS)"
}

# list_sets <dir> <prefix>: names of the final (complete) backup sets, oldest first. A `.partial`
# directory is never listed: it is an unfinished or failed run.
list_sets() {
    local dir="$1" prefix="$2" entry
    [[ -d "$dir" ]] || return 0
    for entry in "$dir"/"$prefix"-*; do
        if [[ -d "$entry" && "$entry" != *.partial ]]; then
            basename "$entry"
        fi
    done | sort
}

# set_timestamp <set name>: the UTC timestamp part of relio-db-<id> / relio-keys-<id>.
set_timestamp() { printf '%s' "${1##*-}"; }

# id_to_epoch <yyyymmddThhmmssZ>
id_to_epoch() {
    local id="$1"
    [[ "$id" =~ ^[0-9]{8}T[0-9]{6}Z$ ]] || return 1
    date -u -d "${id:0:4}-${id:4:2}-${id:6:2} ${id:9:2}:${id:11:2}:${id:13:2} UTC" +%s
}
