# architecture.md — shape of the system

## Monorepo layout (`/src`)
```
src/
  Askwerk.sln
  backend/
    Askwerk.Api/            ASP.NET Core host: App API `/api/app` and Public API `/api/v1` (controllers in feature folders), OpenAPI generation for both, OAuth token endpoint, SignalR, authentication (ASP.NET Core Identity, API keys, optional OIDC/LDAP), MCP server (`Mcp/`), static hosting of the apps
    Askwerk.Domain/         entities, value objects, survey schema, expression language (C# evaluator)
    Askwerk.Application/    use cases, validation, authorisation policies, import/export converters, job handlers
    Askwerk.Infrastructure/ EF Core + Npgsql, migrations, job queue, email, file storage, webhook outbox
    Askwerk.Workers/        background job host (can run inside Api for small installs)
    Askwerk.HealthProbe/    tiny native binary for the container healthcheck (calls /health/ready)
    Askwerk.Tests.*/        Domain, Application, Integration (Testcontainers), Api (endpoints + contract) — see Test architecture
  frontend/
    packages/schema/          survey JSON schema types, expression parser + TS evaluator, i18n layer (shared by all apps)
    packages/ui/              design system: tokens, primitives, components, icons, charts, the /dev/states page
    packages/embed/           embed script for host pages (script and iframe, postMessage events)
    packages/telemetry/       frontend logs, Web Vitals and API metrics, trace context (own module, no SDK; see observability.md)
    packages/app-client/      client for the App API, generated from its OpenAPI document (used by designer and runner only)
    apps/designer/            authoring, publishing, configuration, analytics (admin SPA)
    apps/runner/              respondent app: tiny, embeddable, WCAG 2.2 AA
    apps/site/                marketing/landing (optional, later)
  sdks/
    typescript/, csharp/, python/   generated from the OpenAPI spec, zero runtime dependencies
  e2e/                        Playwright end-to-end flows against the real stack
  pnpm-workspace.yaml, package.json, tsconfig.base.json
```
Analytics ships inside `designer` as a route group with its own lazy bundle; it becomes its own app only if bundle size or team size demands it. The Public API's OpenAPI spec is committed at `docs/api/openapi.json`; the App API's spec is generated at build and feeds `packages/app-client`.

## Runtime components
The default install is two containers: **Askwerk** (one image: API, workers, apps) and **PostgreSQL**. Caddy is an optional third. Nothing else is required.
- **API** (.NET): one service with two surfaces: the App API at `/api/app` for the designer and runner, and the Public API at `/api/v1` for integrators (see Public API below). OpenAPI 3.1 generated at build for each. Realtime (live counts, co-editing presence) via SignalR.
- **Workers**: webhook delivery, email invitations and reminders, import/export jobs, scheduled survey open/close, purge jobs. Jobs are rows in our own PostgreSQL job table (`run_at`, status, attempts, backoff, `last_error`), claimed with `FOR UPDATE SKIP LOCKED` and processed by a .NET `BackgroundService`. A job is written in the same transaction as the change that causes it (transactional outbox), so a saved response and its webhook can never drift apart. Jobs are visible on a jobs screen and audited. No job framework, no Redis. Fallback if the own queue ever proves insufficient: Quartz.NET behind the same interface, webhooks still through the outbox table.
- **PostgreSQL 16+**: the only database. Row-level security per tenant. Responses partitioned per survey or per month when volumes require.
- **File storage**: behind `IStorageProvider`, for file-upload answers, generated exports and theme assets. Default: a folder on a Docker volume. An S3-compatible endpoint (e.g. Hetzner Object Storage for the hosted service) is an optional provider configured in the GUI. No storage container in the install.
- **Authentication**: built in. ASP.NET Core Identity provides local accounts, password hashing, TOTP two-factor, passkeys, lockout and password reset by email. External sign-in is optional and configured per tenant in the GUI: generic OpenID Connect (covers Entra ID, Google, ADFS, Okta, Keycloak and others) and LDAP/Active Directory via `System.DirectoryServices.Protocols`. SAML is added only when a customer needs it, as its own work item. Identity-provider group claims map to roles.
- **Reverse proxy**: Askwerk serves plain HTTP and runs behind any reverse proxy (nginx, Traefik, a cloud load balancer). The default Compose file includes Caddy (Apache 2.0) as an optional profile, because it issues certificates automatically, including on demand for customer domains (see Publishing). With another proxy, certificates are the operator's job and the Domains screen says so.
- **Email**: any SMTP server, configured in the GUI, sent through `IEmailProvider`. Without SMTP, invitations are shown as copyable links and an admin resets passwords.

## Hosting: hosted and on-prem from the same artifacts
- One Docker Compose file is the unit of deployment: `askwerk` (workers can run as a second container of the same image), `postgres`, optional `caddy`. Hosted on Hetzner uses the same images with managed volumes and backups; on-prem customers get the same Compose plus an `askwerk.env` with only secrets (everything else is configured in the GUI and stored in the database).
- Helm chart later, generated from the same images.
- Zero-config first run: on first start the API detects an empty database, runs migrations, and opens a setup wizard in the GUI (first admin account, instance name, base domain, email (optional), external sign-in (optional)). No file editing.

## Publishing to customer domains (must be one screen)
1. Each tenant has a system subdomain for free: `{tenant}.{base-domain}` (wildcard DNS + wildcard certificate on the base domain).
2. A tenant adds a custom domain in Settings → Domains: enters `surveys.customer.at`, sees the CNAME target and a verification TXT record, clicks Verify. The API checks DNS, marks the domain verified, and Caddy's on-demand TLS issues the certificate on first request (the API answers Caddy's `ask` endpoint with verified domains only).
3. Per survey, Publish shows the link on every verified domain, QR code, embed snippet and PDF on one screen. Choosing a domain is a dropdown, defaulting to the tenant's primary domain.
4. Email sender domains are verified the same way (SPF/DKIM records shown, verified in GUI).

## Open-source install (must work for a stranger in 10 minutes)
- The Dockerfiles are in the repo; `docker compose build` builds everything locally without any registry.
- Release images are built only by our CI from those Dockerfiles, signed with build attestations, and published to GitHub Container Registry (`ghcr.io/civauva/askwerk`), semver tags plus `latest`, multi-arch (amd64, arm64). The optional proxy is the official Caddy image with our `deploy/Caddyfile` mounted; there is no own proxy image. The image names are variables in the Compose file (`ASKWERK_IMAGE`, `ASKWERK_PROXY_IMAGE`), so an operator can mirror them to their own registry with one setting. The repository is github.com/civauva/Askwerk.
- `docker compose up -d` with the committed `deploy/docker-compose.yml` and a copied `.env.example` is the whole install. Defaults are production-safe: random secrets generated on first start if absent, PostgreSQL included, files on a volume, local accounts built in, Caddy for TLS unless the operator brings a proxy.
- First start runs migrations and opens the setup wizard in the browser. Upgrades: pull new images, `docker compose up -d`; migrations run automatically and are always backward-compatible within a major version.
- `deploy/` also holds a minimal single-container variant for trials (SQLite is NOT supported; PostgreSQL stays the only database) and the Helm chart once it exists.
- Backup and restore are documented commands (`bin/backup.sh`, `bin/restore.sh`) that cover the database and the files volume (settings live in the database).

## App API and Public API (how the GUI and a stranger's system talk to Askwerk)
Two surfaces, one application. The designer and runner call the App API (`/api/app`); integrators, the SDKs and the MCP server call the Public API (`/api/v1`), which exposes a deliberately chosen subset. Both are thin controllers over the same application handlers, so logic, authorisation and audit are never duplicated. The binding rules (what is public, shape, scopes, versioning, event catalogue) are in `public-api.md`; this section describes how it is built.

```
 designer / runner ──(app-client, cookie or respondent session)──► /api/app ─┐
 integrator / SDKs ──(API key or OAuth token)─────────────────────► /api/v1 ─┤
 MCP clients ───────(API key)─────────────────────────────────────► /mcp ────┤
                                                                             ▼
   Askwerk.Api: authentication → rate limit → controller → authorisation policy
                → application handler → EF Core (RLS: app.tenant_id) → PostgreSQL
                                               │   same transaction
                                               ├──► AuditLog row
                                               └──► Job row (webhook, email, export)
                                                         ▼
                                   Askwerk.Workers ──► webhook POST (HMAC) to the integrator
```

- **Request pipeline** (`Askwerk.Api`): authentication handlers resolve the caller to a user or client, its tenant and its scopes (cookie or respondent session on `/api/app` only; API key or OAuth access token on `/api/v1` and `/mcp` only); ASP.NET Core's rate limiter applies per-tenant and per-key limits; the attribute-routed controller (in its feature folder; App API and Public API controllers sit side by side there) validates the request; a named authorisation policy checks role and scope; `If-Match` is checked against the entity's ETag on definitions and settings; an `Idempotency-Key` is looked up so a retried create or submission returns the original result; the application handler does the work; the tenant id comes only from the authenticated caller and is set on the database connection for row-level security.
- **Versioning**: only the Public API is versioned. The version is the path prefix; `v1` changes only additively; a breaking change creates `v2` controllers next to `v1`, both served until the `v1` sunset date (`public-api.md`). The App API is unversioned and changes with the GUI.
- **Authentication paths**, all in the same process, no external service:
  - Browser users (App API only): ASP.NET Core Identity cookie session with anti-forgery protection. Respondents in the runner get a short-lived respondent session scoped to one collector.
  - API keys: created in Settings → API, stored as a hash, looked up by prefix, scoped, optionally limited to a workspace.
  - OAuth 2.0 client credentials: `POST /api/v1/oauth/token` checks the client secret and issues a short-lived signed JWT; the signing key is generated on first start, stored encrypted in the database and rotated from the GUI. Validation uses Microsoft's JWT bearer handler.
- **Contract**: the Public API's OpenAPI 3.1 document is generated from its controllers and XML docs at build time, served at `/api/v1/openapi.json`, committed at `docs/api/openapi.json` and snapshot-tested; a diff in the snapshot is a public API change. The App API's document is generated the same way but not published or snapshot-reviewed; it only feeds `packages/app-client`.
- **SDK pipeline**: CI regenerates the TypeScript, C# and Python SDKs (`src/sdks/`) from the committed spec, runs each SDK's tests against a freshly started API, and publishes them to npm, NuGet and PyPI with the release. The SDKs are regenerated only when the Public API changes. The designer and runner do not use the SDKs; they use `packages/app-client`.
- **Webhooks**: an event is written as a `Job` row in the same transaction as the change that raises it (transactional outbox); a worker delivers it with an HMAC signature, retries with backoff and records every attempt in `WebhookDelivery`; the GUI shows deliveries and can replay them.
- **Realtime**: SignalR hubs for live counts and co-editing presence, authenticated like the API; not part of the public contract.
- **Embed**: `packages/embed` loads the runner in an iframe (or mounts it via script) on any host page and relays `postMessage` events (`askwerk:started`, `askwerk:page`, `askwerk:completed`, `askwerk:resize`).
- **MCP server**: `/mcp` inside `Askwerk.Api`, a thin layer mapping MCP tools to the same application handlers, with API-key authentication, scopes and audit. AGPL as part of the API; only example client code is Apache-2.0.
- **Survey JSON schema**: `packages/schema/askwerk.schema.json` describes survey definitions, so other tools can produce and consume them; import and export use the same schema.
- **Documentation**: `docs/api/` holds the spec, the endpoint reference, authentication guide, webhook catalogue, embed events, error catalogue and versioning policy; `docs/integrations/` holds a plain curl walkthrough first, then examples (n8n, Zapier, HubSpot, Dynamics). Examples are not dependencies.

## Configuration principle
Every setting has a home in the GUI (instance → tenant → workspace → survey), is stored in PostgreSQL, is validated on save, and takes effect without restart. Environment variables hold only secrets and the database connection. If a new feature needs a setting, it needs a settings screen in the same work item.

## Cross-cutting
- Observability and audit: OpenTelemetry for logs, metrics and traces on the backend (OTLP to any backend, JSON console logs without one), the own `packages/telemetry` module in the apps forwarding through `/api/app/telemetry`, `/health` endpoints, a Diagnostics screen, and the audit log in PostgreSQL. All of it in `observability.md`.
- Security: tenant_id on every table with RLS; policy-based authorisation with a resource handler per entity; rate limits per tenant and token; HMAC-signed webhooks; CSP on the runner; uploads checked by type and size.
- GDPR: anonymity mode per survey, retention policy per tenant, erasure job, DPA template in `/docs`.

## Test architecture
Every layer is built test-first (`testing.md` holds the rules); this is where the tests live and how they run.
- **Backend**: `src/backend/Askwerk.Tests.Domain` (pure unit tests), `Askwerk.Tests.Application` (use cases with real domain and hand-written fakes at the edges), `Askwerk.Tests.Integration` (PostgreSQL via Testcontainers or `ASKWERK_TEST_DB`: persistence, RLS, jobs, webhooks, auth), `Askwerk.Tests.Api` (`WebApplicationFactory`: every endpoint per role and per tenant, OpenAPI snapshots with our own helper), golden files for import/export in `Askwerk.Tests.Application/golden/<format>/`.
- **Shared expression corpus**: `src/frontend/packages/schema/tests/corpus/*.json`, run by both the C# and the TypeScript evaluator.
- **Frontend**: tests sit next to the code they test (`*.test.ts(x)`) in `packages/schema`, `packages/ui`, `packages/embed`, `apps/designer` and `apps/runner`, run by Vitest with Testing Library; the apps reach the App API through `packages/app-client` with an in-memory fake transport.
- **End to end**: `src/e2e/` holds the Playwright flows (designer, runner in both layouts, publishing, import/export, settings), run against the real Askwerk image and PostgreSQL in phone and desktop viewports, DE and EN, with axe in every flow; Lighthouse CI and the bundle-size check for the runner.
- **SDKs**: each `src/sdks/<language>/tests/` runs against a freshly started API.
- **Deployment**: `deploy/tests/` holds the container smoke test (`docker compose build`, `up`, `/health`, setup wizard reachable; run on every PR that touches a Dockerfile or `deploy/`), the full smoke test (sample survey end to end, also without Caddy and without SMTP), the upgrade test and the backup/restore test.
- **Running them**: `bin/test-backend.sh`, `bin/test-frontend.sh`, `bin/test-e2e.sh`, `bin/test-deploy.sh` and `bin/test-all.sh` are what a contributor runs locally and what CI runs; no CI-only steps. CI runs `bin/test-all.sh --full`: backend and frontend in parallel, the image built once (`bin/build-image.sh`), then the browser flows and the smoke test against it. Locally, `bin/test-changed.sh` runs only the test projects and packages the changes can reach (testing.md, How much to run), the browser flows run against `dotnet run` with Docker used only for PostgreSQL (or none with `ASKWERK_TEST_DB`); `--full` runs before every merge. Each test database is a copy of a template migrated once per run (AWK-18).
- **CI pipeline** (every PR; any red stage blocks the merge):
  1. Static: format check, lint, type-check, SPDX headers, dependency audit.
  2. Unit: backend domain and application tests, frontend Vitest suites, expression corpus in both languages.
  3. Integration: PostgreSQL integration tests, API tests, OpenAPI snapshot.
  4. Build: the Askwerk image from the Dockerfile; `packages/app-client` regenerated; SDKs regenerated when the public spec changed; container smoke test when a Dockerfile or `deploy/` changed.
  5. System: Playwright end-to-end with axe, SDK tests, embed and MCP tests, all against the image just built; Lighthouse CI and the bundle budget on PRs that touch the runner or `packages/ui`.
  6. Release candidates only: full deployment smoke, upgrade and backup/restore tests.
