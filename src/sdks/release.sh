#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Builds the three SDK packages into artifacts/sdks/. It never publishes: uploading to npm, NuGet and PyPI is done by
# hand, with the maintainers' accounts.
#   src/sdks/release.sh
set -euo pipefail

sdks="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$sdks/../.." && pwd)"
out="$repo/artifacts/sdks"
rm -rf "$out"
mkdir -p "$out"

node "$sdks/generate.mjs" --check

# TypeScript: compile to dist/, pack the tarball.
cp "$sdks/README.md" "$sdks/typescript/README.md"
(cd "$sdks/typescript" && rm -rf dist && "$repo/src/node_modules/.bin/tsc" -p . && npm pack --pack-destination "$out" >/dev/null)
rm "$sdks/typescript/README.md"

# C#: the NuGet package.
dotnet pack "$sdks/csharp/Askwerk.Sdk/Askwerk.Sdk.csproj" -c Release -o "$out" -v q

# Python: the wheel, with the standard library only.
python -I "$sdks/python/build_wheel.py" "$out" >/dev/null

ls -1 "$out"
