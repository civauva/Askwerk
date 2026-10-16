# SPDX-License-Identifier: AGPL-3.0-only
# Shared by the bin/test-*.sh scripts (sourced, not run).

# ASKWERK_TEST_DB: an owner connection string (Npgsql format) for a local PostgreSQL whose role may create
# databases and roles. Without it the tests start PostgreSQL in Docker. On Windows a value set as a user
# environment variable is picked up even when this shell started before it was set.
if [ -z "${ASKWERK_TEST_DB:-}" ] && command -v powershell.exe >/dev/null 2>&1; then
  ASKWERK_TEST_DB="$(powershell.exe -NoProfile -Command "[Environment]::GetEnvironmentVariable('ASKWERK_TEST_DB','User')" 2>/dev/null | tr -d '\r')"
fi
export ASKWERK_TEST_DB="${ASKWERK_TEST_DB:-}"
[ -n "$ASKWERK_TEST_DB" ] || unset ASKWERK_TEST_DB

# Runs the given layers' scripts at the same time; each layer's output is shown in one piece when it ends.
# Usage: run_layers <layer>...   (bin/test-<layer>.sh); returns non-zero if any layer failed.
run_layers() {
  local bin_dir logs pids=() layers=("$@") failed=() status i
  bin_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  logs="$(mktemp -d)"

  for layer in "${layers[@]}"; do
    ( start=$SECONDS; "$bin_dir/test-$layer.sh" >"$logs/$layer.log" 2>&1; status=$?; echo "== test-$layer: exit $status after $((SECONDS - start)) s" >>"$logs/$layer.log"; exit $status ) &
    pids+=($!)
  done

  for i in "${!layers[@]}"; do
    if wait "${pids[$i]}"; then status=0; else status=1; failed+=("${layers[$i]}"); fi
    echo "== test-${layers[$i]} ==" >&2
    cat "$logs/${layers[$i]}.log" >&2
  done

  rm -rf "$logs"

  if [ "${#failed[@]}" -gt 0 ]; then
    echo "Failed layers: ${failed[*]}" >&2
    return 1
  fi
}
