#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Runs all .NET tests: Domain, Application, Integration (PostgreSQL), Api (endpoints + contract).
# PostgreSQL comes from ASKWERK_TEST_DB if set (a local server, no Docker), otherwise from Testcontainers.
# CI runs exactly this script (agents/architecture.md, Test architecture).
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. "$repo_root/bin/lib/test-env.sh"

dotnet test --solution src/Askwerk.slnx
