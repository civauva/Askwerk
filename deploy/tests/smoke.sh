#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Smoke test of a clean install (agents/testing.md, Deployment): builds the image from the committed
# Dockerfile, starts deploy/docker-compose.yml with a fresh project and volumes, and checks that
# Askwerk becomes healthy, serves the app, reports a ready database and an intact audit chain.
# Runs without Caddy and without SMTP, as the default install does. Tears everything down afterwards.
set -euo pipefail
# A page is read whole before it is searched: grep -q stops early, and under pipefail a cut-off curl fails the pipe.

deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
project="askwerk-smoke-$$"
port="${ASKWERK_SMOKE_PORT:-18080}"
base="http://localhost:$port"

export POSTGRES_PASSWORD="smoke-$(date +%s)-$RANDOM"
export ASKWERK_DATA_KEY=""
export ASKWERK_HTTP_PORT="$port"
export ASKWERK_IMAGE="${ASKWERK_IMAGE:-askwerk:smoke}"

compose() {
  docker compose --project-name "$project" --project-directory "$deploy_dir" -f "$deploy_dir/docker-compose.yml" "$@"
}

cleanup() {
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

fail() {
  echo "SMOKE FAIL: $1" >&2
  compose logs --no-color --tail 60 askwerk >&2 || true
  exit 1
}

# An authenticator app in a few lines (RFC 6238: HMAC-SHA1, 30-second steps, 6 digits).
totp() {
  local python=python3
  "$python" -c "" 2>/dev/null || python=python
  "$python" -c '
import base64, hashlib, hmac, struct, sys, time
key = base64.b32decode(sys.argv[1] + "=" * (-len(sys.argv[1]) % 8))
digest = hmac.new(key, struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
offset = digest[-1] & 15
print("%06d" % ((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1000000))
' "$1"
}

# bin/test-all.sh --full builds the image once and sets ASKWERK_SKIP_BUILD.
if [ "${ASKWERK_SKIP_BUILD:-}" != "1" ]; then
  echo "== build" >&2
  compose build askwerk
fi

echo "== up" >&2
compose up -d

echo "== wait for the container healthcheck" >&2
for _ in $(seq 1 90); do
  state="$(docker inspect --format '{{.State.Health.Status}}' "$(compose ps -q askwerk)" 2>/dev/null || echo starting)"
  [ "$state" = "healthy" ] && break
  sleep 2
done
[ "$state" = "healthy" ] || fail "askwerk container did not become healthy (last state: $state)"

echo "== checks" >&2
curl -fsS "$base/health/ready" >/dev/null || fail "/health/ready is not 200"
body="$(curl -fsS "$base/")" && grep -q '<div id="root"></div>' <<<"$body" || fail "/ does not serve the designer app"
body="$(curl -fsS "$base/api/app/status")" && grep -q '"database":"ready"' <<<"$body" || fail "/api/app/status does not report a ready database"

# The audit chain is for the instance owner only: refused while signed out, intact once the owner is set up.
[ "$(curl -sS -o /dev/null -w '%{http_code}' "$base/api/app/audit/verify")" = "401" ] || fail "/api/app/audit/verify answers without a signed-in owner"
jar="$(mktemp)"
owner() { curl -fsS -b "$jar" -c "$jar" -H 'Content-Type: application/json' "$@"; }
owner -X POST "$base/api/app/setup" -d '{"owner":{"name":"Smoke Owner","email":"owner@smoke.test","password":"correct horse battery"},"instance":{"name":"Smoke","baseAddress":"'"$base"'","defaultLanguage":"en","timeZone":"UTC"},"organisation":{"name":"Smoke","slug":"smoke"}}' >/dev/null || fail "setup did not complete"
key="$(owner "$base/api/app/account/two-factor" | sed -E 's/.*"sharedKey":"([A-Z2-7]+)".*/\1/')"
code="$(totp "$key")"
owner -X POST "$base/api/app/account/two-factor" -d '{"code":"'"$code"'"}' >/dev/null || fail "two-factor setup for the owner failed"
owner "$base/api/app/audit/verify" | grep -q '"isIntact":true' || fail "the instance audit chain is not intact"
rm -f "$jar"
body="$(curl -fsS "$base/api/v1/openapi.json")" && grep -q '"Askwerk Public API"' <<<"$body" || fail "the Public API document is not served"

echo "SMOKE OK" >&2
