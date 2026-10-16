#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Builds the Askwerk image once as askwerk:test; the browser flows and the smoke test then reuse it
# (ASKWERK_IMAGE=askwerk:test ASKWERK_SKIP_BUILD=1). Package downloads are cached by BuildKit between builds.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
DOCKER_BUILDKIT=1 docker build -f "$repo_root/src/Dockerfile" -t askwerk:test "$repo_root"
