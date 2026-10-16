#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Measures the production build (AWK-21): builds the image once, starts it with a fresh PostgreSQL, sets up an owner
# and a published survey, then prints API times and page loads (cold and warm, with transferred kilobytes).
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
deploy_dir="$repo_root/deploy"
project="askwerk-measure-$$"
port="${ASKWERK_MEASURE_PORT:-18090}"

export POSTGRES_PASSWORD="measure-$(date +%s)-$RANDOM"
export ASKWERK_DATA_KEY=""
export ASKWERK_HTTP_PORT="$port"
export ASKWERK_IMAGE="askwerk:test"

compose() {
  docker compose --project-name "$project" --project-directory "$deploy_dir" -f "$deploy_dir/docker-compose.yml" "$@"
}
trap 'compose down --volumes --remove-orphans >/dev/null 2>&1 || true' EXIT

[ "${ASKWERK_SKIP_BUILD:-}" = "1" ] || "$repo_root/bin/build-image.sh" >/dev/null 2>&1
compose up -d --no-build --wait --wait-timeout 180 >/dev/null 2>&1

cd "$repo_root/src"
corepack pnpm --filter @askwerk/e2e exec node measure.mjs "http://localhost:$port"
