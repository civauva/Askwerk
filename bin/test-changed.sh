#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# End of a step: runs only the tests the changes since main (or --since <ref>) can affect, committed or not, backend and frontend at
# the same time. No Docker image, no browser flows: those run once per merge with bin/test-all.sh --full (CI too).
#   backend:  only the test projects that depend on the touched projects (built once, then run side by side)
#   frontend: only the touched packages and the packages that depend on them, and in each only the test files
#             whose imports reach a changed file (vitest --changed); type-checking for the same packages
set -uo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. "$repo_root/bin/lib/test-env.sh"

# --since <ref>: compare with that commit instead of main, e.g. --since HEAD for the changes not yet committed.
base="$(git merge-base HEAD origin/main 2>/dev/null || echo HEAD)"
[ "${1:-}" = "--since" ] && [ -n "${2:-}" ] && base="$2"
changed="$( { git diff --name-only "$base"; git ls-files --others --exclude-standard; } | sort -u)"
start=$SECONDS

# --- backend: touched project → the test projects that can see it -------------------------------------------
declare -A projects=()
add() { for project in "$@"; do projects[$project]=1; done; }
while IFS= read -r file; do
  case "$file" in
    src/backend/Askwerk.Domain/*|src/frontend/packages/schema/tests/corpus/*|src/frontend/packages/schema/presets/*)
      add Domain Application Integration Api ;;
    src/backend/Askwerk.Application/*) add Application Integration Api ;;
    src/backend/Askwerk.Infrastructure/*) add Integration Api ;;
    src/backend/Askwerk.Api/*|src/backend/Askwerk.HealthProbe/*|src/frontend/packages/app-client/openapi.json) add Api ;;
    src/backend/Askwerk.Tests.Integration/*) add Integration Api ;;
    src/backend/Askwerk.Tests.*/*) project="${file#src/backend/Askwerk.Tests.}"; add "${project%%/*}" ;;
    src/Directory.*|src/Askwerk.slnx|global.json) add Domain Application Integration Api ;;
  esac
done <<< "$changed"

backend() {
  local list=("$@") pids=() status=0 project
  dotnet build src/Askwerk.slnx -v q -nologo >/dev/null || { dotnet build src/Askwerk.slnx -v q -nologo; return 1; }

  for project in "${list[@]}"; do
    ( cd "src/backend/Askwerk.Tests.$project" && dotnet run --no-build >"/tmp/askwerk-test-$project.log" 2>&1 ) &
    pids+=($!)
  done

  for i in "${!list[@]}"; do
    if ! wait "${pids[$i]}"; then
      status=1
      grep -v '^{' "/tmp/askwerk-test-${list[$i]}.log" | grep -E -A12 '\[FAIL\]' >&2
    fi
    echo "   Tests.${list[$i]}: $(grep -E 'Total:' "/tmp/askwerk-test-${list[$i]}.log" | tail -1 | sed 's/^ *//')" >&2
  done

  return $status
}

# --- frontend: touched packages and their dependents, related test files only --------------------------------
frontend() {
  cd "$repo_root/src"
  export COREPACK_ENABLE_DOWNLOAD_PROMPT=0
  corepack pnpm install --frozen-lockfile --prefer-offline >/dev/null
  # "...[base]": packages changed since base plus every package that depends on them.
  corepack pnpm -r --filter "...{./frontend/**}[$base]" typecheck &&
    corepack pnpm -r --filter "...{./frontend/**}[$base]" exec vitest run --changed "$base" --passWithNoTests
}

status=0
pids=()
names=()

if [ "${#projects[@]}" -gt 0 ]; then
  echo "Backend: Tests.$(echo "${!projects[@]}" | sed 's/ /, Tests./g')" >&2
  ( backend "${!projects[@]}" ) & pids+=($!); names+=(backend)
fi

if echo "$changed" | grep -qE '^src/(frontend/|package\.json|pnpm-)'; then
  echo "Frontend: packages touched since main and their dependents" >&2
  ( frontend >/tmp/askwerk-test-frontend.log 2>&1 || { grep -E '×|FAIL|error TS' /tmp/askwerk-test-frontend.log >&2; exit 1; } ) & pids+=($!); names+=(frontend)
fi

for i in "${!pids[@]}"; do
  wait "${pids[$i]}" || { status=1; echo "Failed: ${names[$i]}" >&2; }
done

[ "${#pids[@]}" -eq 0 ] && echo "No unit or API tests are affected." >&2
echo "$changed" | grep -qE '^(src/e2e/|deploy/|src/Dockerfile|src/frontend/apps/)' && echo "Browser flows and the image are checked once before the merge: bin/test-all.sh --full." >&2
echo "== affected tests: $((SECONDS - start)) s" >&2
exit "$status"
