# syntax=docker/dockerfile:1
# SPDX-License-Identifier: AGPL-3.0-only
# The Askwerk image: API, workers and the built apps in one container (agents/architecture.md).
# Build context is the repository root (so .editorconfig and global.json apply). Built locally by `docker compose build` and by CI for releases.

# --- 1. Frontend: build the designer app and the runner (served at /s/) --------------------------------------------------------
FROM node:24-alpine AS frontend
WORKDIR /src
RUN corepack enable
COPY src/package.json src/pnpm-lock.yaml src/pnpm-workspace.yaml src/tsconfig.base.json ./
COPY src/frontend frontend
# The Developers hub (AWK-34) bundles the guides from docs/.
COPY docs/api /docs/api
COPY docs/integrations /docs/integrations
RUN --mount=type=cache,id=askwerk-pnpm,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile
RUN pnpm --filter @askwerk/designer --filter @askwerk/runner build

# --- 2. Backend: publish the API and the native health probe ------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG TARGETARCH
# Native AOT for the health probe needs a C toolchain.
# Packages are cached by BuildKit between builds (bin/build-image.sh).
RUN --mount=type=cache,id=askwerk-apt,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,id=askwerk-apt-lists,target=/var/lib/apt/lists,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean \
    && apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev
WORKDIR /src
COPY .editorconfig global.json /
COPY src/Askwerk.slnx src/Directory.Build.props src/Directory.Packages.props ./
COPY src/backend backend
# The looks (W26) live with the shared schema; the sample surveys embed them.
COPY src/frontend/packages/schema/presets frontend/packages/schema/presets
# The Developers hub (AWK-34) embeds the public contract and the example project.
COPY docs/api/openapi.json /docs/api/openapi.json
COPY docs/integrations/sql-server-sync /docs/integrations/sql-server-sync
# NuGet packages are cached between builds; both projects publish in one step, for the target platform only,
# so native libraries (SkiaSharp) come for this platform and not for all of them.
RUN --mount=type=cache,id=askwerk-nuget,target=/root/.nuget/packages \
    rid=$([ "$TARGETARCH" = "arm64" ] && echo linux-arm64 || echo linux-x64) \
    && dotnet publish backend/Askwerk.Api -c Release -r "$rid" --self-contained false -o /out/app \
    && dotnet publish backend/Askwerk.HealthProbe -c Release -r "$rid" -o /out/probe
RUN mkdir -p /out/data

# --- 3. Runtime: chiseled, non-root, no shell --------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
ARG ASKWERK_VERSION=0.0.0-dev
ARG ASKWERK_COMMIT=
WORKDIR /app
COPY --from=backend /out/app .
COPY --from=backend /out/probe/Askwerk.HealthProbe /app/askwerk-health
COPY --from=frontend /src/frontend/apps/designer/dist /app/wwwroot
COPY --from=frontend /src/frontend/apps/runner/dist /app/wwwroot/s
COPY --from=backend --chown=$APP_UID:$APP_UID /out/data /var/lib/askwerk/files
# Time zones (AWK-29, W24): closing dates are entered in the instance's IANA time zone; the chiseled image has no
# zone files, so the build stage's tzdata (Ubuntu, public domain) is copied in. No package, no extra service.
COPY --from=backend /usr/share/zoneinfo /usr/share/zoneinfo
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASKWERK_DATA_DIR=/var/lib/askwerk/files \
    ASKWERK_WEB_ROOT=/app/wwwroot \
    ASKWERK_VERSION=$ASKWERK_VERSION \
    ASKWERK_COMMIT=$ASKWERK_COMMIT
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=30 CMD ["/app/askwerk-health"]
ENTRYPOINT ["dotnet", "Askwerk.Api.dll"]
