#!/usr/bin/env bash
# Restore drill for a self-hosted Relio instance (REL-005, issue #119).
#
# Proves that Relio can be rebuilt from its encrypted backups alone, in a separate environment:
#
#   1. start a throwaway "source" instance (the real docker-compose.yml, backup profile on)
#   2. write synthetic protected data into it (Relio.Data.IntegrationTests, phase Seed)
#   3. prove the backup failure signal works, then take the real encrypted backup
#   4. destroy the source instance and its volumes
#   5. start a fresh "restore" instance (new volumes, a different SQL Server password), restore ONLY
#      from the backup directories, the age identity and the data protection password
#   6. wait for /health/ready (startup authenticates every protected row with the restored keys)
#   7. verify exact plaintext, row counts and schema version (phase Verify)
#   8. prove a backup without its key material cannot serve the data (negative control)
#
# Usage: deploy/restore-drill/drill.sh <step> | all
#   steps: build-tests prepare source-up seed backup-failure-signal backup source-down restore
#          restore-up verify negative-control evidence cleanup
#
# Everything the drill needs is generated per run (random passwords, throwaway age identity, random
# canary) and, on GitHub Actions, masked. Nothing here reads or writes real data. The two stacks use
# fixed container names, so they run one after the other, never together. Requires Docker (compose
# v2), curl, openssl and the .NET SDK. See docs/operations/backup-restore.md.
set -Eeuo pipefail
umask 077

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DRILL_DIR="${RELIO_DRILL_DIR:-${RUNNER_TEMP:-/tmp}/relio-drill}"
COMPOSE_FILE="$REPO_ROOT/docker-compose.yml"
TEST_PROJECT="$REPO_ROOT/Relio.Data.IntegrationTests/Relio.Data.IntegrationTests.csproj"

SOURCE_PROJECT="relio-drill-source"
RESTORE_PROJECT="relio-drill-restore"
APP_IMAGE="ghcr.io/kasuken/relio:latest"
BACKUP_IMAGE="ghcr.io/kasuken/relio-backup:latest"
APP_URL="http://localhost:8080"
SQL_PORT=1433

IDENTITY_FILE="$DRILL_DIR/identity/relio-backup.agekey"
DB_BACKUP_DIR="$DRILL_DIR/backups/database"
KEY_BACKUP_DIR="$DRILL_DIR/backups/keys"
EVIDENCE_DIR="$DRILL_DIR/evidence"
STEPS_FILE="$DRILL_DIR/steps.tsv"
DATA_EVIDENCE="$EVIDENCE_DIR/data-evidence.json"
SOURCE_ENV="$DRILL_DIR/source.env"
RESTORE_ENV="$DRILL_DIR/restore.env"
SECRETS_ENV="$DRILL_DIR/secrets.env"

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# ---------------------------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------------------------

log() { printf 'drill: %s\n' "$*" >&2; }

fail() {
    if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
        printf '::error::%s\n' "$*"
    fi
    printf 'drill: ERROR %s\n' "$*" >&2
    exit 1
}

# mask <value>: hide a generated secret in GitHub Actions logs (a no-op elsewhere).
mask() {
    if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
        printf '::add-mask::%s\n' "$1"
    fi
}

utc_now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

# compose <stack> [compose arguments...]: the real compose file, one project per stack.
compose() {
    local stack="$1" project envfile
    shift
    if [[ "$stack" == "source" ]]; then
        project="$SOURCE_PROJECT"
        envfile="$SOURCE_ENV"
    else
        project="$RESTORE_PROJECT"
        envfile="$RESTORE_ENV"
    fi
    docker compose -f "$COMPOSE_FILE" -p "$project" --env-file "$envfile" "$@"
}

# secret <NAME>: read a generated secret from secrets.env (kept out of the environment and logs).
secret() {
    local line
    line="$(grep -m1 "^$1=" "$SECRETS_ENV")" || fail "secret $1 is missing; run the prepare step first"
    printf '%s' "${line#*=}"
}

random_password() { printf 'Aa1!%s' "$(openssl rand -hex 16)"; }

wait_ready() {
    local deadline=$((SECONDS + 300)) started=$SECONDS
    until curl -fsS -o /dev/null -m 5 "$APP_URL/health/ready" 2>/dev/null; do
        if ((SECONDS >= deadline)); then
            log "relio did not become ready within 5 minutes; its log (the app never logs content):"
            docker logs --tail 80 relio-app >&2 || true
            fail "the application did not reach /health/ready"
        fi
        sleep 3
    done
    printf '%s' "$((SECONDS - started))"
}

# copy_volume <volume> <destination>: copy a Docker volume to a host directory owned by the runner.
copy_volume() {
    mkdir -p "$2"
    docker run --rm --user root -v "$1:/src:ro" -v "$2:/dst" --entrypoint sh "$BACKUP_IMAGE" \
        -c 'cp -a /src/. /dst/ && chown -R "$0:$1" /dst' "$(id -u)" "$(id -g)"
}

# trx_count <file> <attribute>: a counter from a Visual Studio test results file.
trx_count() {
    sed -n "s/.*<Counters[^>]* $2=\"\([0-9]*\)\".*/\1/p" "$1" | sed -n '1p'
}

# run_phase <Seed|Verify> <connection> <keyRingDir> <certificatePath> <expectedPassed> <trxName>
run_phase() {
    local phase="$1" connection="$2" keys="$3" certificate="$4" expected="$5" trx_name="$6"
    local results="$EVIDENCE_DIR/trx" passed failed
    mkdir -p "$results"
    RELIO_RESTORE_DRILL_PHASE="$phase" \
        RELIO_RESTORE_DRILL_CONNECTION="$connection" \
        RELIO_RESTORE_DRILL_KEY_RING_PATH="$keys" \
        RELIO_RESTORE_DRILL_CERTIFICATE_PATH="$certificate" \
        RELIO_RESTORE_DRILL_CERTIFICATE_PASSWORD="$(secret DP_PASSWORD)" \
        RELIO_RESTORE_DRILL_CANARY="$(secret CANARY)" \
        RELIO_RESTORE_DRILL_EVIDENCE_PATH="$DATA_EVIDENCE" \
        dotnet test "$TEST_PROJECT" --configuration Release --no-build \
        --filter "Category=RestoreDrill" \
        --results-directory "$results" --logger "trx;LogFileName=$trx_name"
    passed="$(trx_count "$results/$trx_name" passed)"
    failed="$(trx_count "$results/$trx_name" failed)"
    # A skipped phase would "pass" with nothing proven, so the counts are checked, not the exit code.
    [[ "$passed" == "$expected" && "$failed" == "0" ]] \
        || fail "the $phase phase passed ${passed:-0} of $expected expected tests with ${failed:-?} failures"
    log "the $phase phase passed $passed tests"
}

assert_no_leftover_containers() {
    local name
    for name in relio-app relio-sqlserver relio-backup relio-drill-negative; do
        if [[ -n "$(docker ps -aq --filter "name=^${name}\$")" ]]; then
            fail "a container named $name already exists; the drill needs those fixed names (stop that stack first)"
        fi
    done
}

# set_dir <dir> <prefix>: the single finished backup set in a directory.
single_set() {
    local dir="$1" prefix="$2" count
    local -a found=()
    mapfile -t found < <(find "$dir" -maxdepth 1 -mindepth 1 -type d -name "$prefix-*" ! -name '*.partial' | sort)
    count=${#found[@]}
    ((count == 1)) || fail "expected exactly one finished $prefix set in the backup directory, found $count"
    printf '%s' "${found[0]}"
}

# ---------------------------------------------------------------------------------------------
# Steps
# ---------------------------------------------------------------------------------------------

step_build_tests() {
    dotnet build "$TEST_PROJECT" --configuration Release
}

step_prepare() {
    command -v docker >/dev/null || fail "docker is required"
    command -v openssl >/dev/null || fail "openssl is required"
    mkdir -p "$DRILL_DIR" "$DRILL_DIR/identity" "$DB_BACKUP_DIR" "$KEY_BACKUP_DIR" "$EVIDENCE_DIR"
    : >"$STEPS_FILE"
    assert_no_leftover_containers

    local sa_source sa_restore dp_password canary
    sa_source="$(random_password)"
    sa_restore="$(random_password)"
    dp_password="$(random_password)"
    canary="$(openssl rand -hex 12)"
    local value
    for value in "$sa_source" "$sa_restore" "$dp_password" "$canary"; do
        mask "$value"
    done
    {
        printf 'SA_SOURCE=%s\n' "$sa_source"
        printf 'SA_RESTORE=%s\n' "$sa_restore"
        printf 'DP_PASSWORD=%s\n' "$dp_password"
        printf 'CANARY=%s\n' "$canary"
    } >"$SECRETS_ENV"

    # Build the backup image first: it carries age, which generates the throwaway identity.
    {
        printf 'MSSQL_SA_PASSWORD=%s\n' "$sa_source"
        printf 'RELIO_DP_PASSWORD=%s\n' "$dp_password"
    } >"$SOURCE_ENV"
    compose source --profile backup build relio-backup

    docker run --rm --entrypoint age-keygen "$BACKUP_IMAGE" >"$IDENTITY_FILE" 2>/dev/null
    local private_key public_key
    private_key="$(grep -m1 '^AGE-SECRET-KEY-' "$IDENTITY_FILE")" || fail "age-keygen did not produce an identity"
    public_key="$(sed -n 's/^# public key: //p' "$IDENTITY_FILE" | sed -n '1p')"
    [[ "$public_key" =~ ^age1[0-9a-z]{58}$ ]] || fail "age-keygen did not produce a public key"
    mask "$private_key"

    local stack
    for stack in source restore; do
        local envfile sa
        if [[ "$stack" == "source" ]]; then
            envfile="$SOURCE_ENV"
            sa="$sa_source"
        else
            envfile="$RESTORE_ENV"
            sa="$sa_restore"
        fi
        {
            printf 'MSSQL_SA_PASSWORD=%s\n' "$sa"
            # The same certificate password in both stacks: it is not part of a backup.
            printf 'RELIO_DP_PASSWORD=%s\n' "$dp_password"
            printf 'RELIO_BACKUP_AGE_RECIPIENTS=%s\n' "$public_key"
            printf 'RELIO_BACKUP_DIR=%s\n' "$DB_BACKUP_DIR"
            printf 'RELIO_KEY_BACKUP_DIR=%s\n' "$KEY_BACKUP_DIR"
            printf 'RELIO_BACKUP_RUN_ON_START=false\n'
            # The archives are handed to the runner user so the drill can read the manifests.
            printf 'RELIO_BACKUP_FILE_OWNER=%s:%s\n' "$(id -u)" "$(id -g)"
        } >"$envfile"
    done
    log "prepared a throwaway environment in $DRILL_DIR"
}

step_source_up() {
    assert_no_leftover_containers
    if ! compose source --profile backup up -d --build; then
        compose source --profile backup logs --tail 60 >&2 || true
        fail "the source instance did not start"
    fi
    wait_ready >"$DRILL_DIR/source-ready-seconds"
    [[ "$(docker inspect -f '{{.State.Running}}' relio-backup)" == "true" ]] || fail "the backup container is not running"
    log "the source instance is ready"
}

step_seed() {
    rm -rf "$DRILL_DIR/source-relio"
    copy_volume "${SOURCE_PROJECT}_relio-data" "$DRILL_DIR/source-relio"
    run_phase Seed \
        "Server=localhost,$SQL_PORT;Database=Relio;User Id=sa;Password=$(secret SA_SOURCE);Encrypt=False;TrustServerCertificate=True;" \
        "$DRILL_DIR/source-relio/keys" "$DRILL_DIR/source-relio/certificate.pfx" 1 seed.trx
    # From here on the key material exists only inside the backup archives.
    rm -rf "$DRILL_DIR/source-relio"
}

step_backup_failure_signal() {
    # A backup that cannot reach SQL Server must fail loudly and turn the health status red.
    if compose source exec -T -e SQLCMDPASSWORD=wrong-password relio-backup relio-backup once 2>"$DRILL_DIR/failure-signal.log"; then
        fail "a backup with a wrong SQL Server password reported success"
    fi
    if compose source exec -T relio-backup relio-backup status >"$DRILL_DIR/failure-status.log" 2>&1; then
        fail "the backup status stayed healthy after a failed backup"
    fi
    log "the failure signal works: the failed backup exited non-zero and the status turned unhealthy"
}

step_backup() {
    local started=$SECONDS
    compose source exec -T relio-backup relio-backup once
    compose source exec -T relio-backup relio-backup status

    local db_set key_set
    db_set="$(single_set "$DB_BACKUP_DIR" relio-db)"
    key_set="$(single_set "$KEY_BACKUP_DIR" relio-keys)"
    [[ -s "$db_set/database.bak.gz.age" && -s "$db_set/manifest.json" ]] || fail "the database set is incomplete"
    [[ -s "$key_set/key-material.tar.gz.age" && -s "$key_set/manifest.json" ]] || fail "the key-material set is incomplete"

    # The archives are age files, and the canary must not appear anywhere in them.
    [[ "$(head -c 21 "$db_set/database.bak.gz.age")" == "age-encryption.org/v1" ]] || fail "the database archive is not an age file"
    if grep -aqF -- "$(secret CANARY)" "$db_set/database.bak.gz.age" "$key_set/key-material.tar.gz.age"; then
        fail "the synthetic canary appears in an encrypted archive"
    fi
    # Nothing readable may sit next to the archives.
    [[ -z "$(find "$DB_BACKUP_DIR" "$KEY_BACKUP_DIR" -type f ! -name '*.age' ! -name manifest.json)" ]] \
        || fail "unexpected plain files in the backup directories"

    mkdir -p "$EVIDENCE_DIR"
    cp "$db_set/manifest.json" "$EVIDENCE_DIR/manifest-database.json"
    cp "$key_set/manifest.json" "$EVIDENCE_DIR/manifest-key-material.json"
    printf '%s' "$((SECONDS - started))" >"$DRILL_DIR/backup-seconds"
    log "backup complete: $(basename "$db_set") and $(basename "$key_set")"
}

step_source_down() {
    compose source --profile backup down --volumes --remove-orphans
    [[ -z "$(docker volume ls -q --filter "name=${SOURCE_PROJECT}_")" ]] || fail "source volumes survived the teardown"
    [[ -z "$(docker ps -aq --filter "name=^relio-")" ]] || fail "source containers survived the teardown"
    log "the source instance and its volumes are destroyed"
}

step_restore() {
    assert_no_leftover_containers
    local started=$SECONDS
    # Fresh SQL Server, fresh volumes, a different SA password: only the backups, the identity and
    # the data protection password cross over.
    compose restore up -d --wait sqlserver
    compose restore --profile restore run --rm -T \
        -v "$IDENTITY_FILE:/run/secrets/relio-backup-identity:ro" \
        relio-restore --latest
    printf '%s' "$((SECONDS - started))" >"$DRILL_DIR/restore-seconds"
}

step_restore_up() {
    compose restore up -d relio
    wait_ready >"$DRILL_DIR/restore-ready-seconds"
    log "the restored instance reached /health/ready after $(cat "$DRILL_DIR/restore-ready-seconds") seconds"
}

step_verify() {
    rm -rf "$DRILL_DIR/restored-relio"
    copy_volume "${RESTORE_PROJECT}_relio-data" "$DRILL_DIR/restored-relio"
    run_phase Verify \
        "Server=localhost,$SQL_PORT;Database=Relio;User Id=sa;Password=$(secret SA_RESTORE);Encrypt=False;TrustServerCertificate=True;" \
        "$DRILL_DIR/restored-relio/keys" "$DRILL_DIR/restored-relio/certificate.pfx" 4 verify.trx
    rm -rf "$DRILL_DIR/restored-relio"
}

step_negative_control() {
    # What a restore from the database backup alone (no key material) would look like: a new
    # certificate and key ring against the restored data. It must refuse to start.
    local envfile="$DRILL_DIR/negative.env" status exit_code
    {
        printf 'ASPNETCORE_ENVIRONMENT=Production\n'
        printf 'ConnectionStrings__Relio=Server=sqlserver,1433;Database=Relio;User Id=sa;Password=%s;Encrypt=False;TrustServerCertificate=True\n' "$(secret SA_RESTORE)"
        printf 'Database__ApplyMigrationsOnStartup=false\n'
        printf 'DataProtection__ApplicationName=Relio\n'
        printf 'DataProtection__KeyRingPath=/tmp/keys\n'
        printf 'DataProtection__ProtectionMode=Certificate\n'
        printf 'DataProtection__Certificate__Path=/tmp/certificate.pfx\n'
        printf 'DataProtection__Certificate__Password=%s\n' "$(random_password)"
        printf 'DataProtection__AutoGenerateIfMissing=true\n'
        printf 'Email__Provider=None\n'
        printf 'Billing__Provider=None\n'
    } >"$envfile"

    docker rm -f relio-drill-negative >/dev/null 2>&1 || true
    docker run -d --name relio-drill-negative --network "${RESTORE_PROJECT}_relio-network" --env-file "$envfile" "$APP_IMAGE" >/dev/null

    local deadline=$((SECONDS + 180))
    while true; do
        status="$(docker inspect -f '{{.State.Status}}' relio-drill-negative)"
        [[ "$status" == "running" || "$status" == "created" ]] || break
        if ((SECONDS >= deadline)); then
            docker rm -f relio-drill-negative >/dev/null 2>&1 || true
            fail "negative control: the instance with a new key ring kept running instead of refusing the data"
        fi
        sleep 3
    done
    exit_code="$(docker inspect -f '{{.State.ExitCode}}' relio-drill-negative)"
    docker logs relio-drill-negative >"$DRILL_DIR/negative-control.log" 2>&1 || true
    docker rm -f relio-drill-negative >/dev/null 2>&1 || true
    [[ "$exit_code" != "0" ]] || fail "negative control: the instance with a new key ring exited successfully"
    # It must have failed for the right reason (the protected-data check), not because SQL Server
    # was unreachable.
    grep -q 'SensitiveFieldBackfillException' "$DRILL_DIR/negative-control.log" \
        || fail "negative control: the instance failed, but not at protected-data verification"
    log "negative control: a new key ring refused the restored data (exit code $exit_code)"
    printf '%s' "$exit_code" >"$DRILL_DIR/negative-exit-code"
}

# ---------------------------------------------------------------------------------------------
# Evidence and cleanup
# ---------------------------------------------------------------------------------------------

read_or() { if [[ -f "$1" ]]; then cat "$1"; else printf '%s' "$2"; fi; }

step_evidence() {
    mkdir -p "$EVIDENCE_DIR"
    local commit run_url event steps_json
    commit="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || printf 'unknown')"
    run_url=""
    if [[ -n "${GITHUB_RUN_ID:-}" ]]; then
        run_url="${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-}/actions/runs/$GITHUB_RUN_ID"
    fi
    event="${GITHUB_EVENT_NAME:-local}"
    steps_json="$(awk -F'\t' 'BEGIN { printf "[" } NR > 1 { printf "," } { printf "{\"name\":\"%s\",\"status\":\"%s\",\"seconds\":%s}", $1, $2, $3 } END { printf "]" }' "$STEPS_FILE" 2>/dev/null || printf '[]')"
    [[ -n "$steps_json" ]] || steps_json="[]"

    local app_image backup_image
    app_image="$(docker image inspect -f '{{.Id}}' "$APP_IMAGE" 2>/dev/null || printf 'unknown')"
    backup_image="$(docker image inspect -f '{{.Id}}' "$BACKUP_IMAGE" 2>/dev/null || printf 'unknown')"

    {
        printf '{\n'
        printf '  "generatedUtc": "%s",\n' "$(utc_now)"
        printf '  "commit": "%s",\n' "$commit"
        printf '  "event": "%s",\n' "$event"
        printf '  "runUrl": "%s",\n' "$run_url"
        printf '  "sourceReadySeconds": %s,\n' "$(read_or "$DRILL_DIR/source-ready-seconds" null)"
        printf '  "backupSeconds": %s,\n' "$(read_or "$DRILL_DIR/backup-seconds" null)"
        printf '  "restoreSeconds": %s,\n' "$(read_or "$DRILL_DIR/restore-seconds" null)"
        printf '  "restoredReadySeconds": %s,\n' "$(read_or "$DRILL_DIR/restore-ready-seconds" null)"
        printf '  "negativeControlExitCode": %s,\n' "$(read_or "$DRILL_DIR/negative-exit-code" null)"
        printf '  "images": { "relio": "%s", "relioBackup": "%s" },\n' "$app_image" "$backup_image"
        printf '  "steps": %s,\n' "$steps_json"
        printf '  "databaseManifest": %s,\n' "$(read_or "$EVIDENCE_DIR/manifest-database.json" null)"
        printf '  "keyMaterialManifest": %s,\n' "$(read_or "$EVIDENCE_DIR/manifest-key-material.json" null)"
        printf '  "data": %s\n' "$(read_or "$DATA_EVIDENCE" null)"
        printf '}\n'
    } >"$EVIDENCE_DIR/restore-drill-evidence.json"

    if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
        {
            printf '## Restore drill\n\n'
            # shellcheck disable=SC2016 # backticks are Markdown, not command substitution
            printf 'Commit `%s`, event `%s`, %s\n\n' "$commit" "$event" "$(utc_now)"
            printf '| Step | Result | Seconds |\n| --- | --- | --- |\n'
            awk -F'\t' '{ printf "| %s | %s | %s |\n", $1, $2, $3 }' "$STEPS_FILE" 2>/dev/null || true
            # shellcheck disable=SC2016
            printf '\nRestored instance ready after **%s** seconds. Evidence: the `restore-drill-evidence` artifact.\n' \
                "$(read_or "$DRILL_DIR/restore-ready-seconds" "n/a")"
        } >>"$GITHUB_STEP_SUMMARY"
    fi
    log "evidence written to $EVIDENCE_DIR/restore-drill-evidence.json"
}

step_cleanup() {
    local stack
    for stack in source restore; do
        local envfile="$DRILL_DIR/$stack.env"
        if [[ -f "$envfile" ]]; then
            compose "$stack" --profile backup --profile restore down --volumes --remove-orphans >/dev/null 2>&1 || true
        fi
    done
    docker rm -f relio-drill-negative >/dev/null 2>&1 || true
    # The evidence directory is kept; everything that held a key, a password or a backup is removed.
    rm -rf "$DRILL_DIR/identity" "$DRILL_DIR/backups" "$DRILL_DIR/source-relio" "$DRILL_DIR/restored-relio" \
        "$DRILL_DIR/negative.env" "$SOURCE_ENV" "$RESTORE_ENV" "$SECRETS_ENV"
    log "cleaned up"
}

# ---------------------------------------------------------------------------------------------
# Dispatch: every step records its result and duration for the evidence
# ---------------------------------------------------------------------------------------------

STEP_NAME=""
STEP_STARTED=0

record_step() {
    local rc=$?
    trap - EXIT
    if [[ -n "$STEP_NAME" && -d "$DRILL_DIR" ]]; then
        printf '%s\t%s\t%s\n' "$STEP_NAME" "$([[ $rc == 0 ]] && echo passed || echo failed)" "$((SECONDS - STEP_STARTED))" >>"$STEPS_FILE"
    fi
    exit "$rc"
}

usage() {
    cat >&2 <<'EOF'
usage: deploy/restore-drill/drill.sh <step> | all
steps: build-tests prepare source-up seed backup-failure-signal backup source-down restore
       restore-up verify negative-control evidence cleanup
EOF
    exit 2
}

main() {
    local step="${1:-}"
    case "$step" in
    all)
        local each
        for each in build-tests prepare source-up seed backup-failure-signal backup source-down restore restore-up verify negative-control; do
            "${BASH_SOURCE[0]}" "$each" || {
                "${BASH_SOURCE[0]}" evidence || true
                "${BASH_SOURCE[0]}" cleanup || true
                exit 1
            }
        done
        "${BASH_SOURCE[0]}" evidence
        "${BASH_SOURCE[0]}" cleanup
        ;;
    build-tests | prepare | source-up | seed | backup-failure-signal | backup | source-down | restore | restore-up | verify | negative-control)
        STEP_NAME="$step"
        STEP_STARTED=$SECONDS
        trap record_step EXIT
        "step_${step//-/_}"
        ;;
    evidence | cleanup)
        "step_$step"
        ;;
    *) usage ;;
    esac
}

main "$@"
