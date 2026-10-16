#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Runs the Playwright flows (src/e2e) on phone and desktop, in German and English, against a fresh Askwerk:
#   locally (default): the API with `dotnet run`, serving the freshly built designer app, on a new database of
#     the ASKWERK_TEST_DB server, or of a throwaway PostgreSQL container (Docker only for the database)
#   in Docker (ASKWERK_E2E_DOCKER=1): the real image and PostgreSQL from deploy/docker-compose.yml under a
#     throwaway project; bin/test-all.sh --full (CI, before a merge) uses this
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
. "$repo_root/bin/lib/test-env.sh"
export COREPACK_ENABLE_DOWNLOAD_PROMPT=0

run_flows() {
  cd "$repo_root/src"
  corepack pnpm install --frozen-lockfile --prefer-offline >/dev/null
  corepack pnpm --filter @askwerk/e2e exec playwright install chromium
  corepack pnpm --filter @askwerk/e2e test
}

if [ "${ASKWERK_E2E_DOCKER:-}" = "1" ]; then
  deploy_dir="$repo_root/deploy"
  project="askwerk-e2e-$$"
  port="${ASKWERK_E2E_PORT:-18081}"
  export POSTGRES_PASSWORD="e2e-$(date +%s)-$RANDOM"
  export ASKWERK_DATA_KEY=""
  export ASKWERK_HTTP_PORT="$port"
  export ASKWERK_IMAGE="${ASKWERK_IMAGE:-askwerk:e2e}"
  export ASKWERK_E2E_URL="http://localhost:$port"

  compose() {
    docker compose --project-name "$project" --project-directory "$deploy_dir" -f "$deploy_dir/docker-compose.yml" "$@"
  }
  trap 'compose down --volumes --remove-orphans >/dev/null 2>&1 || true' EXIT

  if [ "${ASKWERK_SKIP_BUILD:-}" = "1" ]; then
    compose up -d --no-build --wait --wait-timeout 180
  else
    compose up -d --build --wait --wait-timeout 180
  fi

  run_flows
  exit 0
fi

# --- Local: dotnet run against a new database on the ASKWERK_TEST_DB server or a PostgreSQL container ------
port="${ASKWERK_E2E_PORT:-18082}"
database="askwerk_e2e_$$_$RANDOM"
data_dir="$(mktemp -d)"
export ASKWERK_E2E_URL="http://localhost:$port"
container=""
app_pid=""

cleanup() {
  # wait reports the TERM it sent (143); that is the normal end, not a failure.
  [ -n "$app_pid" ] && kill "$app_pid" 2>/dev/null && { wait "$app_pid" 2>/dev/null || true; }
  if [ -n "$container" ]; then
    docker rm -f "$container" >/dev/null 2>&1 || true
  else
    sql "DROP DATABASE IF EXISTS $database WITH (FORCE)" >/dev/null 2>&1 || true
  fi
  rm -rf "$data_dir"
}
trap cleanup EXIT

if [ -z "${ASKWERK_TEST_DB:-}" ]; then
  echo "== start PostgreSQL in a throwaway container" >&2
  password="e2e-$RANDOM$RANDOM"
  container="$(docker run -d --rm -e POSTGRES_PASSWORD="$password" -p 127.0.0.1::5432 postgres:16-alpine)"
  db_port="$(docker port "$container" 5432/tcp | head -1 | sed 's/.*://')"
  ASKWERK_TEST_DB="Host=127.0.0.1;Port=$db_port;Database=postgres;Username=postgres;Password=$password"
  sql() { docker exec "$container" psql -U postgres -q -v ON_ERROR_STOP=1 -c "$1"; }
  for _ in $(seq 1 60); do docker exec "$container" pg_isready -U postgres -h 127.0.0.1 >/dev/null 2>&1 && break; sleep 0.5; done
else
  # psql takes libpq "key=value" pairs; ASKWERK_TEST_DB is in Npgsql format (Host=..;Username=..;...).
  psql_bin="$(command -v psql || ls -d /c/Dev/PostgreSQL/*/bin/psql* "/c/Program Files/PostgreSQL"/*/bin/psql* 2>/dev/null | tail -1 || true)"
  [ -n "$psql_bin" ] || { echo "psql not found; put PostgreSQL's bin directory on PATH" >&2; exit 1; }
  libpq="$(echo "$ASKWERK_TEST_DB" | tr ';' '\n' | sed -E "s/^ *([^=]+)= *(.*)$/\1\t\2/" | awk -F'\t' '
    BEGIN { map["host"]="host"; map["server"]="host"; map["port"]="port"; map["database"]="dbname"; map["username"]="user"; map["user id"]="user"; map["password"]="password" }
    { key=tolower($1); if (key in map) { gsub(/\x27/, "\\\x27", $2); printf "%s=\x27%s\x27 ", map[key], $2 } }')"
  sql() { "$psql_bin" "$libpq" -q -v ON_ERROR_STOP=1 -c "$1"; }
fi

sql "CREATE DATABASE $database"

echo "== build the designer app and the API" >&2
(cd "$repo_root/src" && corepack pnpm install --frozen-lockfile --prefer-offline >/dev/null && corepack pnpm --filter @askwerk/designer --filter @askwerk/runner build >/dev/null)
# The runner is served at /s/ from the same web root, as in the image.
web_root="$data_dir/wwwroot"
cp -r "$repo_root/src/frontend/apps/designer/dist" "$web_root"
cp -r "$repo_root/src/frontend/apps/runner/dist" "$web_root/s"
dotnet build "$repo_root/src/backend/Askwerk.Api" -c Release -v q -nologo >/dev/null

echo "== start Askwerk on $ASKWERK_E2E_URL" >&2
ASKWERK_DB="$ASKWERK_TEST_DB;Database=$database" \
ASKWERK_DATA_DIR="$data_dir" \
ASKWERK_WEB_ROOT="$web_root" \
ASPNETCORE_URLS="$ASKWERK_E2E_URL" \
ASPNETCORE_ENVIRONMENT=Production \
  dotnet run --project "$repo_root/src/backend/Askwerk.Api" -c Release --no-build --no-launch-profile >"$data_dir/askwerk.log" 2>&1 &
app_pid=$!

for _ in $(seq 1 120); do
  curl -fsS "$ASKWERK_E2E_URL/health/ready" >/dev/null 2>&1 && break
  kill -0 "$app_pid" 2>/dev/null || { tail -30 "$data_dir/askwerk.log" >&2; exit 1; }
  sleep 0.5
done
curl -fsS "$ASKWERK_E2E_URL/health/ready" >/dev/null || { echo "Askwerk did not become ready" >&2; tail -30 "$data_dir/askwerk.log" >&2; exit 1; }

run_flows
