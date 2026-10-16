#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Type-checks and runs all Vitest suites of the frontend packages and apps.
# CI runs exactly this script (agents/architecture.md, Test architecture).
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root/src"
export COREPACK_ENABLE_DOWNLOAD_PROMPT=0

corepack pnpm install --frozen-lockfile
corepack pnpm -r --filter "./frontend/**" typecheck
corepack pnpm -r --filter "./frontend/**" test
