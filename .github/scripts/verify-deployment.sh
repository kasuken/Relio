#!/usr/bin/env bash
# Black-box checks for a deployed Relio instance: readiness, TLS, HTTPS redirect, HSTS, security
# headers and (for staging) noindex. Read-only: GET requests only, and no response body is
# printed except robots.txt. Used by .github/workflows/staging.yml; also safe to point at
# production for evidence:
#
#   BASE_URL=https://www.relio.club bash .github/scripts/verify-deployment.sh
#
# Environment:
#   BASE_URL        required, https, no trailing slash
#   SMOKE_PATHS     space-separated paths that must return 200 (default: /Account/Login /pricing)
#   READY_ATTEMPTS  tries of /health/ready, 15 seconds apart (default 40 = 10 minutes: a fresh
#                   database applies every migration on a small SQL tier before the app listens)
#   READY_ONLY      true: stop after /health/ready and /health/live
#   EXPECT_NOINDEX  true: also require robots.txt "Disallow: /" and an X-Robots-Tag noindex header
set -euo pipefail

BASE_URL="${BASE_URL:?BASE_URL is required, e.g. https://app-relio-stg-edff.azurewebsites.net}"
SMOKE_PATHS="${SMOKE_PATHS:-/Account/Login /pricing}"
READY_ATTEMPTS="${READY_ATTEMPTS:-40}"
READY_ONLY="${READY_ONLY:-false}"
EXPECT_NOINDEX="${EXPECT_NOINDEX:-false}"

[[ "$BASE_URL" == https://* && "$BASE_URL" != */ ]] || {
  echo "::error::BASE_URL must start with https:// and have no trailing slash (got $BASE_URL)."
  exit 1
}
host="${BASE_URL#https://}"

failures=0

report() { # report <PASS|FAIL> <message>
  local line="$1: $2"
  echo "$line"
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    echo "- $line" >> "$GITHUB_STEP_SUMMARY"
  fi
  if [[ "$1" == FAIL ]]; then
    echo "::error::$2"
    failures=$((failures + 1))
  fi
}

status_of() { curl -s -o /dev/null -w '%{http_code}' "$1" || true; }

# 1. Readiness. /health/ready covers the database and pending migrations, so a 200 means the
#    instance is connected and its schema is current.
status=000
for attempt in $(seq 1 "$READY_ATTEMPTS"); do
  status=$(status_of "$BASE_URL/health/ready")
  [[ "$status" == 200 ]] && break
  echo "Attempt $attempt/$READY_ATTEMPTS: /health/ready returned $status, retrying in 15s"
  [[ "$attempt" == "$READY_ATTEMPTS" ]] || sleep 15
done
if [[ "$status" == 200 ]]; then
  report PASS "/health/ready returned 200"
else
  report FAIL "/health/ready returned $status after $READY_ATTEMPTS attempts"
  exit 1
fi

status=$(status_of "$BASE_URL/health/live")
if [[ "$status" == 200 ]]; then report PASS "/health/live returned 200"; else report FAIL "/health/live returned $status"; fi

if [[ "$READY_ONLY" == true ]]; then
  [[ "$failures" == 0 ]] || exit 1
  exit 0
fi

# 2. Smoke pages.
for path in $SMOKE_PATHS; do
  status=$(status_of "$BASE_URL$path")
  if [[ "$status" == 200 ]]; then report PASS "$path returned 200"; else report FAIL "$path returned $status"; fi
done

# 3. Plain HTTP redirects to HTTPS.
redirect=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' "http://$host/" || true)
case "$redirect" in
  "301 https://"*|"307 https://"*|"308 https://"*) report PASS "HTTP redirects to HTTPS (${redirect%% *})" ;;
  *) report FAIL "HTTP did not redirect to HTTPS: ${redirect%% *}" ;;
esac

# 4. Response headers of a public page.
headers=$(curl -sS -D - -o /dev/null "$BASE_URL/Account/Login" | tr -d '\r' | tr 'A-Z' 'a-z')

hsts=$(grep -m1 '^strict-transport-security:' <<<"$headers" || true)
if [[ "$hsts" =~ max-age=([0-9]+) && "${BASH_REMATCH[1]}" -gt 0 ]]; then
  report PASS "HSTS present (max-age=${BASH_REMATCH[1]})"
else
  report FAIL "Strict-Transport-Security header missing or max-age is 0"
fi

if grep -q '^x-content-type-options: *nosniff' <<<"$headers"; then
  report PASS "X-Content-Type-Options: nosniff"
else
  report FAIL "X-Content-Type-Options: nosniff missing"
fi
if grep -q '^content-security-policy:' <<<"$headers"; then
  report PASS "Content-Security-Policy present"
else
  report FAIL "Content-Security-Policy missing"
fi

# 5. TLS: curl verifies the certificate chain and the host name, and must be able to negotiate
#    TLS 1.2. Print the issuer and expiry for the evidence record.
if curl --tlsv1.2 -fsS -o /dev/null "$BASE_URL/health/live"; then
  cert=$(echo | openssl s_client -connect "$host:443" -servername "$host" 2>/dev/null \
    | openssl x509 -noout -issuer -enddate 2>/dev/null | tr '\n' ' ' || true)
  report PASS "TLS 1.2 handshake with a valid certificate (${cert:-issuer unavailable})"
else
  report FAIL "TLS 1.2 request with certificate validation failed"
fi
# The runner's OpenSSL may refuse TLS 1.1 on its own, which would make this pass for the wrong
# reason; the workflow also reads minTlsVersion from Azure, which is the authoritative check.
if curl --tls-max 1.1 -sS -o /dev/null "$BASE_URL/health/live" 2>/dev/null; then
  report FAIL "TLS 1.1 or lower was accepted"
else
  report PASS "TLS 1.1 or lower is refused"
fi

# 6. Staging must not be indexed (the app gates indexing on the Production environment).
if [[ "$EXPECT_NOINDEX" == true ]]; then
  robots=$(curl -sS "$BASE_URL/robots.txt" | tr -d '\r')
  if [[ "$robots" == $'User-agent: *\nDisallow: /' ]]; then
    report PASS "robots.txt disallows everything"
  else
    report FAIL "robots.txt is not the disallow-all file: $robots"
  fi
  if curl -sS -D - -o /dev/null "$BASE_URL/" | tr -d '\r' | tr 'A-Z' 'a-z' | grep -q '^x-robots-tag:.*noindex'; then
    report PASS "X-Robots-Tag: noindex on /"
  else
    report FAIL "X-Robots-Tag: noindex missing on /"
  fi
fi

[[ "$failures" == 0 ]] || { echo "$failures check(s) failed."; exit 1; }
echo "All checks passed."
