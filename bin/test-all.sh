#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Runs every test layer and fails if any layer fails. Backend and frontend run in parallel.
#   bin/test-all.sh          locally: backend + frontend, then the browser flows against dotnet run (no Docker
#                            when ASKWERK_TEST_DB is set)
#   bin/test-all.sh --full   CI and before a merge: additionally builds the image once and runs the browser
#                            flows and the smoke test against it, in Docker
set -uo pipefail

bin_dir="$(cd "$(dirname "$0")" && pwd)"
. "$bin_dir/lib/test-env.sh"
start=$SECONDS
status=0

# The full run also tests the SQL Server sync example against SQL Server in a container (AWK-14),
# the three SDKs against a running Askwerk (AWK-26) and the directory sign-in against OpenLDAP (AWK-12).
[ "${1:-}" = "--full" ] && export ASKWERK_TEST_SQLSERVER=1 ASKWERK_TEST_SDKS=1 ASKWERK_TEST_LDAP=1

run_layers backend frontend || status=1

if [ "${1:-}" = "--full" ]; then
  echo "== build the image once ==" >&2
  "$bin_dir/build-image.sh" || exit 1
  export ASKWERK_IMAGE=askwerk:test ASKWERK_SKIP_BUILD=1 ASKWERK_E2E_DOCKER=1
  run_layers e2e deploy || status=1
else
  run_layers e2e || status=1
fi

echo "== all layers: $((SECONDS - start)) s" >&2
[ "$status" -eq 0 ] && echo "All test layers passed." >&2
exit "$status"
