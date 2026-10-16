#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Runs the deployment tests in deploy/tests/ (smoke; upgrade and backup/restore on release candidates).
# CI runs exactly this script (agents/architecture.md, Test architecture).
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
"$repo_root/deploy/tests/smoke.sh"
