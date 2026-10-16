# decisions.md — decision log (append-only)

Format: `## YYYY-MM-DD — Decision` then Reason, Alternatives rejected, Work item, Licence (for dependencies).

## 2026-10-12 — Backend is .NET
Reason: team fluency, enterprise auth and tooling, on-prem story. Alternatives rejected: TypeScript end-to-end (one logic engine, but less fluency), Go, Elixir. Consequence: the expression evaluator exists twice (C# and TypeScript) and shares one golden test corpus. Work item: tbd.

## 2026-10-12 — No commercial or paid third-party components, ever
Reason: owner decision; keeps hosted and on-prem licensing simple. Consequence: SurveyJS Creator/Dashboard/PDF excluded; MediatR and AutoMapper excluded; renderer and designer are built in-house (SurveyJS Form Library, MIT, may be evaluated as a reference only after the first wireframes). Work item: tbd.

## 2026-10-12 — Monorepo
Reason: one schema package shared by API contracts, designer and runner; one CI. Layout in `architecture.md`.

## 2026-10-12 — Visuals before code
Reason: owner decision. Wireframes first; hi-fi only on request; approval in Claude Code; artifacts stored in the Throughly work item comments. Process in `workflow.md`.

## 2026-10-12 — Throughly is the project-management system
my.throughly.app, MCP at my.throughly.app/mcp. All work items and their visuals live there.

## 2026-10-12 — Customers publish to their own domains; configuration lives in the GUI
Reason: owner decision. Mechanism: wildcard system subdomain plus verified custom domains with on-demand TLS via Caddy; all settings in PostgreSQL, editable in the GUI, no file editing. Hosted (Hetzner) and on-prem from the same Compose artifacts.

## 2026-10-12 — Open-source UI primitives are allowed when justified
Radix UI, dnd-kit, motion, Tailwind, TanStack, Lucide (all MIT/ISC). Each gets its own entry when first added, with the justification.

## 2026-10-12 — Two product languages from day one
German and English for the product UI; survey texts are translation maps supporting any language.

## 2026-10-12 — Product name is Askwerk
Reason: "Ask" + "Werk" reads naturally in German and English, is distinctive, and askwerk.com was unregistered at the time of the decision (checked via Verisign RDAP). Alternatives rejected: Surveyvor (surveyvor.com taken since 2010), Fragelo, Anketia, Pollento, Surveyard, Formuro. Logo, colour and typeface are decided through the brand/mockup work item; placeholder tokens in `ui-design-system.md` until then.

## 2026-10-12 — Askwerk is open source, Docker-installable, integration-first
Reason: owner decision. Consequences: public repo with LICENSE, README (10-minute install), CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md, issue and PR templates, CHANGELOG.md; images on GHCR; `docker compose up` is the install; OpenAPI + generated SDKs + webhooks + embed events + JSON schema + MCP are the integration surface (see `architecture.md`). All dependencies must be compatible with the chosen licence and with others' commercial use.

## 2026-10-12 — Licence: AGPL-3.0 for the server, Apache-2.0 for the integration surface
Reason: protect the core against hosted clones that give nothing back, while never imposing copyleft on anyone integrating with Askwerk. Scope:
- AGPL-3.0-only: everything under `src/backend/`, `src/frontend/apps/designer`, `src/frontend/apps/runner`, `src/frontend/packages/ui`, `deploy/`, `bin/` — i.e. the product itself. Root `LICENSE` = AGPL-3.0.
- Apache-2.0: `src/frontend/packages/schema` (survey JSON schema, types, expression parser/evaluator), the embed script, the generated SDKs (TypeScript, C#, Python), the OpenAPI spec, example integrations in `docs/integrations/`, the MCP server client code. Each of these folders carries its own `LICENSE` file and `SPDX-License-Identifier: Apache-2.0` headers.
- Every source file starts with an SPDX header naming its licence; CI fails on a missing header.
- Dependency rule: anything linked into AGPL code must be AGPL-3.0-compatible (MIT, BSD, ISC, Apache-2.0, MPL-2.0, LGPL, GPL-3.0 are fine); anything in the Apache-2.0 packages must be permissive only (no copyleft), so integrators inherit nothing.
- Contributions: Developer Certificate of Origin (DCO sign-off on commits) instead of a CLA; relicensing the Apache parts later is therefore not possible without every contributor's consent, which is intended.
Alternatives rejected: AGPL-3.0 everywhere (would put obligations on the SDK users), Apache-2.0 everywhere (no protection against hosted forks), MIT, BSL/source-available (not open source).

## 2026-10-12 — Integration model: files, API, webhooks, embed; no plugin system
Reason: owner decision — do not over-engineer; Askwerk does the work, external systems only feed it and read it, and must never affect the essence of the app. Import/export is the primary integration and must feel natural to the end user. Alternatives rejected: a three-tier "Apps" model with manifests, synchronous hooks and third-party question types (drafted and withdrawn the same day as over-engineering); in-process runtime plugins; embedded scripting. Details in `extensibility.md`.

## 2026-10-12 — Readability over cleverness
Owner decision: proper whitespace and indentation always, code readable by a human, comments where they make sense (why, not what). Rules in `coding-standards.md`, enforced by `.editorconfig`, `dotnet format` and Prettier in CI.

## 2026-10-12 — Controllers, XML documentation, reuse through inheritance where it makes sense
Owner decision. Attribute-routed controllers everywhere (no minimal APIs): most readable unit of ASP.NET for contributors; they live in the feature folders so vertical slices stay. XML doc comments on all public members feed the OpenAPI descriptions. Base controllers, common DTOs, shared validators and EF configurations are expected wherever they remove real repetition; inheritance for shared behaviour, composition for optional behaviour. Alternatives rejected: minimal APIs (fewer lines, less familiar), copy-paste per controller.

## 2026-10-12 — Engineering principles: security first, performance first, auditable, TDD, snappy
Owner decision, in that priority order when they conflict. Concrete rules in `AGENTS.md` (principles and budgets), `coding-standards.md` (how they become code) and `workflow.md` (definition of done).

## 2026-10-12 — Work item references for the entries above
All entries above were made while writing the instruction set, before Throughly items existed. Their work item is AWK-1 ("Work item: tbd" in the first two entries means AWK-1). Follow-up work: licence files and repo scaffolding AWK-2, brand AWK-3, data model AWK-4. Work item: AWK-1.

## 2026-10-12 — No hard dependencies
Reason: owner decision. Askwerk must install, run, build and be developed without services, SaaS or libraries we cannot replace. Rule: the default install is the Askwerk image plus PostgreSQL, Caddy optional; no open-core libraries whose paid tier we would need, no native binaries; every dependency has a decisions.md entry with licence, why we cannot write it ourselves in reasonable time, the interface it sits behind, and how we would remove it. Details and the approved list in `coding-standards.md` (Dependencies). Supersedes the library list in "Open-source UI primitives are allowed when justified" (motion and the Lucide package are no longer used). Alternatives rejected: case-by-case choices without a rule. Work item: AWK-1.

## 2026-10-12 — Authentication is built in; no identity broker
Reason: an identity broker (Keycloak, Authentik) is a separate, heavy container every install would need, and Authentik is open-core. ASP.NET Core Identity ships with .NET (MIT): local accounts, password hashing, TOTP two-factor, passkeys, lockout, reset by email. External sign-in is optional per tenant in the GUI: generic OIDC (Entra ID, Google, ADFS, Okta, Keycloak…) through ASP.NET Core's OIDC handler, LDAP/AD through `System.DirectoryServices.Protocols`. SAML only when a customer needs it, as its own work item. Consequence: Askwerk stores password hashes, so every PR that touches authentication carries a threat note and tests. Alternatives rejected: Keycloak bundled (heavy, a second system to operate), Authentik (open-core), our own password hashing (never hand-rolled). Supersedes the identity-broker part of `architecture.md` as first written. Work item: AWK-1.

## 2026-10-12 — Background jobs: own PostgreSQL job table
Reason: jobs (webhooks, email, import/export, scheduled open/close, purge) must be written in the same transaction as the change that causes them (transactional outbox); a library that writes through its own connection cannot guarantee that. Our needs are small: queue, retry with backoff, run at a timestamp, single execution, a jobs screen, audit entries. Implementation: `Job` table claimed with `FOR UPDATE SKIP LOCKED`, processed by a .NET `BackgroundService`; payloads are versioned records. Alternatives rejected: Hangfire (open-core with a paid Pro tier, community PostgreSQL storage, serialised method calls couple the code to the queue), Quartz.NET (sound, Apache-2.0, but no shared transaction and cron is not needed; kept as documented fallback behind the same interface). Work item: AWK-1.

## 2026-10-12 — File storage on the filesystem by default; S3 optional; no MinIO
Reason: storage is needed only for file-upload answers, generated exports and theme assets; a folder on a Docker volume covers all three. MinIO stripped its community edition and stopped publishing images in 2025, so it is no longer a safe base. Implementation: `IStorageProvider`, default filesystem; an S3-compatible endpoint is an optional provider set in the GUI (e.g. Hetzner Object Storage for the hosted service). Backup is database plus files volume. Alternatives rejected: MinIO in the Compose file, S3 required, files in PostgreSQL (bloats the database and its backups). Work item: AWK-1.

## 2026-10-12 — Caddy is the default front proxy, not a requirement
Reason: Caddy issues certificates automatically, including on demand for verified customer domains, which keeps publishing to a customer domain one screen. Askwerk itself serves plain HTTP and works behind any proxy; with another proxy the operator handles certificates. Alternatives rejected: Kestrel with our own ACME client (security-critical code to maintain), Caddy required. Licence: Apache-2.0. Work item: AWK-1.

## 2026-10-12 — Own i18n layer; no i18n framework
Reason: typed DE/EN catalogues plus the browser's native `Intl` APIs cover plural rules, numbers and dates in about 150 lines, keep the runner small and make a missing translation a compile error. It is the same mechanism as survey translation maps. Alternatives rejected: Lingui (good, but a build step and a dependency we do not need), i18next (larger runtime, string keys instead of text). Work item: AWK-1.

## 2026-10-12 — Libraries removed from the plan
Reason: no hard dependencies; each is replaced by what .NET or the browser provides, or by a small piece of our own. FluentAssertions → xUnit `Assert` (FluentAssertions 8+ is under a commercial licence since 2025). FluentValidation → DataAnnotations, `IValidatableObject` and own validators. Mapperly → hand-written mapping. Serilog → built-in logging plus OpenTelemetry. ReadStat (native) → CSV plus generated SPSS/Stata syntax. motion → CSS transitions and View Transitions API. Zustand → React state and context. react-hook-form → React 19 form actions, zod and an own form helper. Recharts/visx → own SVG charts with table views. Lucide package → copied SVGs. Storybook → `/dev/states` page with Playwright and axe. Kept: EF Core + Npgsql, MailKit, OpenTelemetry, xUnit, Testcontainers, Verify, React, TypeScript, Vite, Tailwind, TanStack Router/Query, Radix UI, dnd-kit, zod. Work item: AWK-1.

## 2026-10-12 — Code on GitHub, images built by our CI, registry replaceable
Reason: open-source contributors are on GitHub, but nothing in the product or the build may require it. Dockerfiles are in the repo and `docker compose build` works without a registry. Release images are built only by our CI, signed with build attestations and published to GHCR under an org Dražan owns (`askwerk`, availability to be confirmed); image names are Compose variables so they can be mirrored to any registry. CI steps are scripts in `/bin`. Throughly stays the maintainers' tracker; GitHub issues and PRs are the public intake. Alternatives rejected: a self-hosted registry from day one (extra operations for no gain yet). Work item: AWK-1.

## 2026-10-12 — Licence scope clarified
Clarifies "Licence: AGPL-3.0 for the server, Apache-2.0 for the integration surface". Apache-2.0 folders: `src/frontend/packages/schema`, `src/frontend/packages/embed`, `src/sdks/`, `docs/api/` (OpenAPI spec), `docs/integrations/`. The MCP server is part of the API and is AGPL-3.0; only example MCP client code is Apache-2.0. The list of compatible dependency licences in that entry is the only one; `coding-standards.md` links to it. Work item: AWK-1.

## 2026-10-12 — Process details
Reason: gaps found in the AWK-1 review. The canonical data-model diagram lives in the repo (`docs/architecture/data-model.svg`, from AWK-4), not in an external document. Wireframe and mockup sources live in `design/AWK-N-slug/`; Throughly holds rendered images and links. PRs are squash-merged; the TDD evidence is the PR's commit list. Agents use Throughly's `start_task`, timers and `link_commit`; branch names follow `workflow.md`. Epics are long-lived areas (AWK-8, AWK-9, AWK-10). Work item: AWK-1.

## 2026-10-12 — Pure TDD on every layer
Reason: owner decision. Tests were planned, but the frontend, SDK and deployment parts were only one line each in `coding-standards.md`. Now every layer — domain, persistence, API, jobs, import/export, `packages/schema`, `packages/ui`, designer, runner, SDKs, embed, MCP, Docker install — follows the same red → green → refactor cycle, visible in the PR's commits, with the rules in `testing.md` (new, second in the reading order). Test tools are development-only: xUnit, Testcontainers, Verify, Vitest, Testing Library, Playwright, axe-core, Lighthouse CI. No mocking or assertion libraries; fakes are hand-written. Alternatives rejected: test-after with coverage targets; mocking libraries (Moq, NSubstitute, MSW). Work item: AWK-1.

## 2026-10-12 — The Public API is a first-class product surface
Reason: owner decision. The Public API was spread over `architecture.md`, `coding-standards.md` and `extensibility.md` and was not removed by the AWK-1 edits, but it had no single home, and the removal of the identity broker left API authentication open. `public-api.md` (new, fourth in the reading order) now holds the rules: contract first with the OpenAPI snapshot, API parity with the GUI (the designer uses the same API through the TypeScript SDK), API keys and OAuth 2.0 client credentials issued by Askwerk itself, scopes, rate limits, a versioning and 12-month deprecation policy, webhooks catalogue with HMAC signatures, embed events, zero-dependency SDKs, MCP server, docs. Every public feature ships API, tests, SDKs and docs in one PR. The OAuth authorization-code flow for third-party apps is not in v1. Licence of the JWT package: MIT (Microsoft). Alternatives rejected: OpenIddict as a full OAuth server (more than v1 needs), API keys only (no standard flow for server-to-server integrations). Work item: AWK-1.

## 2026-10-12 — Two API surfaces: App API for our apps, Public API as a chosen subset
Reason: owner decision. The Public API is meant to be a subset of what the product offers, not a mirror of the GUI; Askwerk works on its own and integrations are optional. Rule: the designer and runner call the App API (`/api/app`: cookie or respondent session, unversioned, changes with the GUI, client generated into `packages/app-client`, AGPL-3.0); integrators, the SDKs and the MCP server call the Public API (`/api/v1`: API keys or OAuth client credentials, versioned, snapshot-tested, documented). Both run in the same process and call the same application handlers, so logic, authorisation and audit exist once. An endpoint becomes public only when its work item decides so; only then does a PR carry SDK regeneration, the contract snapshot and `docs/api/`. Initial public scope is listed in `public-api.md`. Supersedes, in "The Public API is a first-class product surface", the parity rule (everything the GUI can do is in the API), the designer using the public SDK, and every public feature shipping API and SDKs in the same PR. Alternatives rejected: one API used by everyone (every GUI change becomes a public contract change; heavy per-PR SDK work), a separate Public API service (a second container, authentication twice, no gain because it would call the same handlers). Work item: AWK-1.

## 2026-10-12 — Consistency fixes from the AWK-1 audit
Backend tests live in the `Askwerk.Tests.*` projects, mirroring the feature folders, not inside the feature folders. Dockerfile changes are driven by a container smoke test (build, up, `/health`, setup wizard) that runs on every PR touching a Dockerfile or `deploy/`; the full smoke, upgrade and backup/restore tests stay on release candidates. Lighthouse and the bundle budget run on PRs that touch the runner or `packages/ui`. Work item: AWK-1.

## 2026-10-12 — Audit and observability from day one, backend and frontend
Reason: owner decision. Auditing, logging and metrics are built into the first runnable skeleton (AWK-2), not added later, on the backend and in both apps. Audit log: PostgreSQL, same transaction as the change, append-only enforced by database permissions, per-tenant hash chain, exports audited, respondents never audit actors. Telemetry: OpenTelemetry .NET (Apache-2.0) on the backend through the .NET abstractions (`ILogger`, `Meter`, `ActivitySource`), OTLP to any backend, JSON console logs and an in-app Diagnostics screen when none is configured; no collector container in the default install. Frontend: an own `packages/telemetry` module (errors, Web Vitals via `PerformanceObserver`, API metrics, W3C `traceparent`) sending to `/api/app/telemetry`, which feeds the backend pipeline. Rules in `observability.md` (new, fourth in the reading order). The OTLP endpoint is the one setting that applies only after a restart. Alternatives rejected: OpenTelemetry browser SDK (too large for the runner budget, more than we need), a bundled collector or Grafana stack (an extra service in every install), third-party error trackers such as Sentry (SaaS or a heavy self-hosted service), audit entries as log records (not transactional, not queryable in the GUI). Licence: OpenTelemetry .NET Apache-2.0. Work item: AWK-1; built in AWK-2.

## 2026-10-12 — Runnable skeleton is its own work item; official Caddy image
Reason: AWK-2 is repository scaffolding without application code, so the runnable skeleton that carries the audit log and telemetry ("built in AWK-2" in the observability entry above) is AWK-11 (Runnable skeleton: API host, health, audit log, telemetry, Dockerfile, smoke test). The optional proxy uses the official Caddy image with the committed `deploy/Caddyfile` mounted instead of an own `askwerk-proxy` image: one image less to build, sign and publish. Contact addresses `security@askwerk.com` and `conduct@askwerk.com` are used in SECURITY.md and CODE_OF_CONDUCT.md (Contributor Covenant 2.1); the domain and both mailboxes must exist before the repository goes public. Alternatives rejected: a code skeleton inside AWK-2 (mixes scaffolding with product code), an own proxy image. Work item: AWK-2.

## 2026-10-12 — No code of conduct file
Reason: owner decision; the repository is about the technical product. Instead of CODE_OF_CONDUCT.md, CONTRIBUTING.md states two rules: be respectful and stay on topic; maintainers may edit or remove comments and block users who are not. Supersedes CODE_OF_CONDUCT.md in "Askwerk is open source, Docker-installable, integration-first" and the Contributor Covenant and `conduct@askwerk.com` in "Runnable skeleton is its own work item; official Caddy image"; `security@askwerk.com` stays. Alternatives rejected: Contributor Covenant 2.1, adding one later when the repository goes public. Work item: AWK-2.

## 2026-10-13 — Runnable skeleton: dependencies added, Verify dropped
Reason: AWK-11 built the first runnable code test-first. Dependencies added, each with its licence and exit path: Npgsql.EntityFrameworkCore.PostgreSQL and Npgsql.OpenTelemetry (PostgreSQL Licence, data access; EF Core is the persistence layer by decision); OpenTelemetry .NET with the ASP.NET Core and HttpClient instrumentation and the OTLP exporter (Apache-2.0; behind `ILogger`/`Meter`/`ActivitySource`, removable at the composition root); Microsoft.AspNetCore.OpenApi (MIT, Microsoft; generates both OpenAPI documents); React and React DOM (MIT); development only: dotnet-ef as a local tool (MIT), Testcontainers (MIT), xUnit v3 (Apache-2.0), Vite and @vitejs/plugin-react (MIT), Vitest and jsdom (MIT), Testing Library (MIT), axe-core and @axe-core/playwright (MPL-2.0, test-only), Playwright (Apache-2.0), openapi-typescript (MIT, generates types only; nothing at runtime). Verify was planned for snapshot tests but now charges a maintenance fee through a build-time sponsorship check, which breaks the no-paid-components rule; a 40-line snapshot helper of our own replaces it (`ASKWERK_UPDATE_SNAPSHOTS=1` rewrites snapshots for review). Tailwind is deferred until real screens need it; the placeholder page uses plain CSS on the `packages/ui` tokens. The web-vitals package is not used; `packages/telemetry` measures with `PerformanceObserver`. Alternatives rejected: Verify with a sponsorship exemption (a licence condition we would have to renew), FluentAssertions-style assertion libraries, MSW for fetch mocking (hand-written fake transports instead). Work item: AWK-11.

## 2026-10-13 — Database roles and readiness
Reason: approved in the AWK-11 wireframe. Migrations run as the database owner (`ASKWERK_DB`); the app connects as `askwerk_app`, whose password is an HMAC of `ASKWERK_DATA_KEY`, so no extra secret exists. `askwerk_app` has only INSERT and SELECT on `audit_log`; the database refuses UPDATE, DELETE and TRUNCATE (tested). Audit entries are chained per tenant (instance-level entries in their own chain) under a transaction-scoped advisory lock and hashed with microsecond timestamps, as PostgreSQL stores them. `/health/ready` and the status endpoint report ready only after migrations and the instance's first audit entry are committed. A blank `ASKWERK_DATA_KEY` is generated on first start and kept in the data volume. Work item: AWK-11.

## 2026-10-13 — Repository is github.com/civauva/Askwerk
Reason: owner decision; the repository was created under Dražan's GitHub account and is public. Image names follow it: `ghcr.io/civauva/askwerk` (registries require lowercase). Supersedes the `askwerk` org name in "Code on GitHub, images built by our CI, registry replaceable"; the image name stays a Compose variable, so moving to an org later is one setting. Work item: AWK-11.

## 2026-10-13 — Integration with a host system that owns users and analytics
Reason: owner answers for AWK-4. The systems Askwerk is integrated with mostly have their own login, store their data in SQL Server, and need Askwerk both standalone and embedded. Consequences: all of it is opt-in per tenant and off by default; Askwerk keeps working with its own accounts and analytics. When a tenant opts in: (1) the main sign-in path is a signed hand-over token from the host app (one-time, short-lived); OIDC (Entra ID, ADFS, any provider), LDAP/Active Directory and a provisioning API are supported as well; Askwerk keeps no passwords for these users. (2) Host users both design and answer surveys; answering comes first if the two must be staged. (3) Data reaches SQL Server by a nightly or scheduled pull ("changed since" via the Public API, long format plus codebook); Askwerk never writes into the host's database and stays PostgreSQL-only; webhooks remain available. (4) Designer and runner can run standalone and embedded in the host product. Alternatives rejected: Askwerk on SQL Server as its own database (PostgreSQL row-level security and JSONB are part of the design), direct writes into the host database (a hard dependency on its schema). Work items: AWK-4, AWK-12, AWK-13, AWK-14.

## 2026-10-13 — Data model approved (AWK-4, W3)
Owner decisions on the entity diagram: (1) users are one account per person across the installation, with a Membership per tenant; (2) typed answer rows are the only source of truth, with no JSON copy of the answers on Response, so analytics and exports query plain SQL; (3) the primary domain is a flag on Domain, at most one per tenant; (4) the audit log is partitioned by month from the start, so retention drops whole partitions and no role needs DELETE on audit rows; responses and answers are partitioned only when load tests call for it; (5) organisation-wide Contacts now, kept in Askwerk or fed from a host system through the Public API, with Participant as contact × survey; (6) the hand-over token is a JWT signed by the host with its private key, Askwerk keeps only the public key and accepts each token once within five minutes; (7) an embedded designer gets its own short-lived session from that token instead of a cookie. `ExternalReference` maps host ids to any Askwerk record; the host owns users, organisations and memberships, Askwerk owns survey content, and deletes from the host become deactivations. Canonical diagram: `docs/architecture/data-model.svg`; rules in `data-model.md`. Work item: AWK-4.

## 2026-10-13 — Brand: Werkstatt
Owner decision after three directions (W1, rejected as too thin: colour swaps of one screen) and a full brand board (W2/W3). Askwerk's brand is "Werkstatt": IBM Plex Sans Condensed, Plex Sans and Plex Mono (OFL, self-hosted: the `@fontsource` packages are bundled by Vite into the apps, no font CDN); signal orange `#d2440f` (dark `#ff7a3d`) on warm paper and graphite, steel blue for information; 2 px / 4 px corners; borders instead of shadows; the measuring rule as signature detail; a square tile with an angular question mark and the wordmark "askwerk" with "werk" in signal; a short, factual voice. Tokens and rules in `ui-design-system.md`; board in `design/AWK-3-brand/`. Alternatives rejected: B "Petrol" (Inter, petrol green), C "Tinte" (Geist, ink blue). Licence: fonts OFL-1.1, packages MIT. Work item: AWK-3.

## 2026-10-13 — Sign-in and tenant isolation as built (AWK-16)
Owner decisions: lockout after 5 failed passwords for 15 minutes; password reset links work once, for 30 minutes; two-factor is required for owners and admins and optional for other roles. Implementation choices: (1) row-level security reads `askwerk.tenant_id` and `askwerk.user_id` from the connection; a connection interceptor sets both from the authenticated session on every open (never from request input); tenant rows are written only for the tenant in context, and a signed-in user without a tenant sees only their own memberships and organisations. (2) A slug check across tenants goes through `tenant_slug_taken`, a `SECURITY DEFINER` function that answers only yes or no. (3) ASP.NET Core Identity runs on our own `app_user` table through a custom `UserStore`; only the authenticator and data-protection token providers are registered. The authenticator key is encrypted with the instance's data-protection keys (kept in the data directory) and recovery codes are stored as SHA-256 hashes. (4) Passwords need 12 characters and nothing else (length over composition rules). (5) Email goes through .NET's own `SmtpClient` with STARTTLS; the SMTP password is encrypted like the authenticator key. (6) The QR code for two-factor setup comes from our own encoder in `packages/ui` (byte mode, level M, versions 1–10), so no QR library is added; it is reused for survey links later. (7) The audit verification endpoint is restricted to the instance owner. Alternatives rejected: Identity's EF Core stores package (a second user schema next to the approved data model), MailKit (a dependency for what the built-in client does), a QR package. No new packages were added. Work item: AWK-16.

## 2026-10-14 — Surveys as built (AWK-15 step 3, W6)
Owner approved W6. Survey gets a `title`, an internal name for the list and the designer, not shown to respondents. `workspace_id` stays empty until workspaces exist, and `theme_id` comes with AWK-17. Otherwise the tables follow W3:
- Every survey table has row-level security on `askwerk.tenant_id`. A unique partial index keeps one draft per survey, and the trigger `survey_version_frozen` refuses any update or delete of a published version, even as the owner.
- Publishing runs `DefinitionChecks` on the server (the same corpus as the designer). It writes the page, question and option index and the variable mappings for renames, then starts the next draft as a copy.
- Draft autosaves are not audited; creating and publishing are, on the organisation's chain.
- The survey link is `{base address}/s/{survey id}` until collectors and domains exist.
Alternatives rejected:
- A title inside the definition: it would version an internal name and show up in exports.
- Deriving the index from drafts: reporting reads only published versions.
Work item: AWK-15.

## 2026-10-14 — Runner server side as built (AWK-6, W8 rev. 2 and M4 approved)
Owner approved W8 (with a welcome page and J/N for Ja/Nein) and the hi-fi M4 for A, B and D.
- Respondents need no account. Opening a link asks `run_survey_tenant(survey_id)`, a SECURITY DEFINER function that answers only for published or closed surveys. The request then runs in that organisation, so row-level security still applies to every query.
- A response is found again through a random resume token in an HttpOnly cookie, scoped to /api/app/run/{survey}. It is kept for 30 days and only its SHA-256 is stored. Another browser starts its own response.
- Every page is checked on the server by `AnswerRules`: required, ranges and steps, option codes, "Other" text, email/phone/URL formats, date limits including "today", matrix rows and ranking order. Completing is refused while required answers are missing; complete responses do not change.
- Layout (classic or one at a time) and the welcome texts are optional fields of the definition, so they are versioned with the survey. They are edited in the designer in a later step.
Alternatives rejected:
- A session cookie per respondent with server state (nothing to clean up with tokens).
- Saving answers as JSON on the response (the data model makes Answer rows the source of truth).
Work item: AWK-6.

## 2026-10-14 — Production speed (AWK-21)
Owner feedback: the UI was not snappy. bin/measure.sh measures the production image (API times, page loads cold and warm, transferred kilobytes).
- The session check no longer loads the user, rebuilds the principal and rewrites the cookie on every request. It compares the cookie's security stamp with the current one, cached for 30 s in `SecurityStamps`. A new stamp (password change, sign out everywhere, two-factor change) is put into the cache at once, so other sessions end on their next request. With several app instances, another instance notices within 30 s.
- Built files are sent with Brotli or gzip. Files under /assets and /s/assets carry content hashes and are cached for a year (immutable); pages are never cached. API answers stay uncompressed (BREACH).
- Result on localhost: cold loads halved, a second visit to the survey list drops from 535 kB to 2 kB, and the cookie is rewritten on 0 of 30 calls instead of 30 of 30.
Work item: AWK-21.


## 2026-10-14 — Survey settings and preview as built (AWK-22, W9 and M5 approved)
- "Survey settings" is the first entry in the designer's outline. It shows the welcome and thank-you pages on the canvas, with their texts edited in place per language. The inspector holds the layout and the welcome page switch. Texts are edited only on the canvas, so the inspector repeats no fields.
- `welcome.enabled: false` switches the welcome page off and keeps its texts. Its texts are then not checked for translations. Without a welcome page, the response starts with the first page saved, never on merely opening the link. An empty thank-you text is removed, so the runner's standard text applies.
- Preview: `GET /api/app/surveys/{id}/preview` gives the last saved draft in the runner's shape. `PUT .../preview/pages/{pageId}` checks a page with the same `AnswerRules`. Both are only for editors of the organisation and store nothing.
- The runner opens the draft at `/s/{id}?preview` with its own client. A banner on every screen says that answers are not saved. In the designer's side panel (an iframe, phone or desktop width, Restart) the panel's header says so instead. Anyone else sees "Survey not found".
Alternatives rejected:
- Storing preview answers flagged as test responses: they would leak into results and exports.
Work item: AWK-22.
## 2026-10-14 — Demo survey in every organisation (AWK-23)
Owner request: for the demo, one fully specified survey is always there; the seeding becomes controllable later.
- `Askwerk.Infrastructure/Seeding/demo-survey.json` is a published survey in German and English. It has a welcome and a thank-you page, 3 pages and 21 questions, and covers every question type (15, with the image block of AWK-17) with Other, Don't know, the rating looks, a slider, a matrix, ranking, date/time and the text formats. The image block shows a picture the seeder draws itself (a gradient in the signal colours) and stores as the organisation's own file. A test keeps it free of any publish-check finding.
- It is created on every start and right after setup, for each organisation that lacks it, as that organisation's first owner, with audit entries by the system. Its id is derived from the organisation's id, so restarts and several instances never add a second one.
- `ASKWERK_DEMO_SURVEY=off` switches the seeding off; the API tests do so, apart from the tests of the demo itself.
Work item: AWK-23.

## 2026-10-14 — PDFs from our own renderer (AWK-17, W10)
Owner decision: PDFs are drawn by our own renderer in .NET with PdfSharp/MigraDoc (MIT), from the survey definition and the theme. They are styled fully from the theme's tokens, with logo, header image, image blocks, picture choices and background images. Email gets the same data as inline styles in a table layout. Custom CSS is a flagged extra for the web only.
Alternatives rejected:
- Headless Chromium in the image: custom CSS would reach the PDF, but the image grows by about 150–300 MB and every PDF needs a browser process.
- Only the browser's Print to PDF: no PDFs from the server, so none attached to emails or exports.
Work item: AWK-17.

## 2026-10-14 — Themes, templates and images approved (AWK-17, W10 rev. 2)
Owner approved W10 rev. 2. Fonts: besides the self-hosted set, organisations may upload their own font files (WOFF2, TTF, OTF; checked by type and size). The licence of an uploaded font is the organisation's responsibility, and the upload dialog says so. Web and PDF use the uploaded font (embedded in the PDF). Email falls back to a named system font, because mail clients do not load web fonts reliably.
Work item: AWK-17.

## 2026-10-14 — Themes and uploaded files as built (AWK-17 step 1)
- Theme tokens have one JSON format, defined in `packages/schema/src/theme.ts` with a C# twin. `checkTheme` and `ThemeChecks` agree on a shared corpus: WCAG ratios for text, muted text, button text and the accent; text over a background overlay is judged against the worst case, a black and a white image; colours, fonts and images are checked too. Errors refuse a save. The presets live as data in `packages/schema/presets`.
- Files are stored by `IFileStorage`, by default a folder `ASKWERK_DATA_DIR/assets`, keyed `{tenant}/{asset}`. The type is told from the first bytes: PNG, JPEG and WebP up to 5 MB, WOFF2, WOFF, TTF and OTF up to 2 MB. SVG is not accepted yet, because of scripts inside SVG and because email and PDF cannot show it well. Resizing comes with the runner's use of images.
- Owners and admins change themes; editors upload files and choose a survey's theme. The runner gets the survey's look with the definition, and the files of that look through `/api/app/run/{survey}/assets/{asset}`. No other file of the organisation is reachable there.
Work item: AWK-17.

## 2026-10-14 — PDF library pinned, native code allowed (AWK-17)
Owner decisions:
- PDFsharp/MigraDoc stays pinned to exactly 6.2.4, the last MIT version. Versions 6.5 and later are AGPL and are never taken. The pin is exact in `Directory.Packages.props`, and a test fails on any other version. Our own PDF writer replaces it once AWK-17 is done, behind the same renderer interface.
- Custom CSS stays web only.
- Native code is no reason to refuse a library; an unneeded dependency or a non-free licence is. This supersedes "no native binaries" in the entry "No hard dependencies". SkiaSharp (MIT, maintained by Microsoft, with native assets) is therefore allowed for resizing and converting uploaded images on the server. It is added in AWK-17 step 3, with the warning about version, licence and last release given to the owner first.
Work item: AWK-17.

## 2026-10-14 — SkiaSharp for uploaded images (AWK-17 step 3)
- SkiaSharp 4.153.1 with SkiaSharp.NativeAssets.Linux.NoDependencies 4.153.1. MIT, maintained by Microsoft, last stable release 2026-09-30. The owner was warned before it was added.
- What it does: every uploaded image is fully decoded, so a file with a correct header but broken or hostile contents is refused. Pictures over 40 megapixels are refused before decoding. The width and height are stored. Smaller copies (480, 960, 1600, 2400 px wide, never larger than the original) are made on first request with `?w=` and kept next to the original.
- Why not our own: decoding and resampling PNG, JPEG and WebP safely is not a reasonable amount of own code.
- Behind `IImageProcessor` (`SkiaImageProcessor`); removing it means one new implementation of that interface.
- Image size: the API is now published for the target platform only, so only that platform's native library is in the image. That is 158 MB instead of 142 MB; without the platform-only publish it was 634 MB.
Work item: AWK-17.

## 2026-10-14 — Images in surveys (AWK-17 step 4)
- Survey settings → Look: the survey's theme (outside the draft, so a published survey changes look at once) and its own header image with alt text per language (in the definition, `headerImage`).
- Image blocks are a question type `image`, never answered and never required; the caption is the question's text and may be empty; `image` holds the file, alt text (required, per language), size (full, wide, half, small) and alignment. Any other question may carry an `image` with alt text. Single and multiple choice options may carry a picture; the option's label names it, so the picture itself is decorative for screen readers.
- Respondents may load exactly the files the published definition and its theme show.
- The command palette lists questions before the "Add question" commands, so a word from a question finds it first.
Work item: AWK-17.

## 2026-10-14 — Survey templates (AWK-17 step 5)
- Askwerk's templates are JSON files embedded in the server (`Askwerk.Infrastructure/Templates`): customer satisfaction, employee pulse, event feedback and NPS short, each in German and English. A test keeps them free of any publish-check finding. Their ids are `askwerk:<name>`. They have no images, so they work in every organisation.
- An organisation's templates live in `survey_template` (row-level security) and are a copy of a survey's draft at the time of saving. Editors save and delete them, via Ctrl K → "Save as template" in the designer.
- A new survey from a template copies the definition, and the template's languages replace the ones chosen. The dialog asks only for the title.
Work item: AWK-17.

## 2026-10-14 — Email from the theme, test send only (AWK-17 step 6)
Owner decision: step 6 builds the email side with a test send only. Sending to respondents comes with distribution (AWK-7).
- Message texts: invitation, reminder and thanks, per language, in `message_template` (row-level security). Askwerk's texts apply until an organisation saves its own. Only the placeholders {name} {link} {deadline} {duration} {organisation} {survey} are accepted.
- `EmailRenderer` draws the email from the survey's theme: table layout, inline styles, no scripts and no web fonts (the theme's font with Arial as fallback). It has the logo, the survey's or the theme's header image, and a button in the accent colour, plus a plain-text part. A paragraph that is only {link} becomes the button. With "Answer the first question in the email", a rating, NPS, yes/no or single-choice question is shown as links to `/s/{survey}?answer={question}:{value}`, and the runner opens with that answer chosen. Images use absolute addresses of the published survey.
- The preview is drawn by the same renderer and shown in a sandboxed iframe. "Send test to me" goes to the signed-in user only, with "[Test]" in the subject, and only once SMTP is set up. `EmailMessage` carries an optional HTML part, sent as an alternative view.
Work item: AWK-17.

## 2026-10-14 — Survey PDFs (AWK-17 step 7)
- PDFsharp-MigraDoc is pinned to exactly `[6.2.4]`, the last MIT version. 6.5.0 on NuGet is AGPL and is never taken. A test checks the pin and the loaded assembly version. Everything that uses it is in `Askwerk.Infrastructure/Pdf/SurveyPdf.cs`, so our own PDF writer can replace it in one place.
- `GET /api/app/surveys/{id}/pdf?language=` gives the current draft as an A4 form in its theme, for everyone in the organisation. The designer opens it from Preview ▾ → As PDF, in the language being edited. The form has:
  - the header image (the survey's own or the theme's), logo, title and welcome text, and the online link;
  - numbered questions in the accent colour;
  - square tick boxes for choices, numbered boxes for rating/NPS/slider, lines for text, a table for the matrix;
  - image blocks with captions, and page numbers.
- Fonts: the IBM Plex WOFF files from Fontsource (OFL, `Pdf/Fonts/OFL.txt`) are embedded in the server, unpacked to TrueType by our own `Woff` converter, and embedded into each PDF. Latin subset only for now. A theme font the PDF cannot embed, such as an uploaded WOFF2, falls back to IBM Plex Sans.
- Images are made at most 1600 px wide; WebP becomes PNG, because the PDF library cannot place WebP.
- A QR code of the survey link sits beside the link. Our own encoder (`QrCode.cs`) is a port of `packages/ui/src/qr.ts`; one golden corpus (`packages/ui/tests/corpus/qr`) keeps browser and server drawing the same modules, so no QR dependency.
Work item: AWK-17.

## 2026-10-14 — Results and exports (AWK-24, W11)
Owner decisions: W11 approved; Excel files by our own writer, no library.
- Results are read by every member of the organisation. The overview aggregates in PostgreSQL (counts per option, numbers, matrix cells, the latest texts) over the filtered responses; the questions are those of the latest published version (or the chosen one), and answers to older versions count by question id.
- The response list loads newest first on a cursor over `(started_at, id)` with its own index, 50 at a time; the grid is built on the Results page until the shared data grid (AWK-19) exists, like the survey list.
- Exports follow `Codebook` (`Askwerk.Domain`): one variable per answer part, "Don't know" as -99. One `IExporter` per format in `Askwerk.Application/Exports`, with golden files. Formats and variables are documented in `docs/api/exports.md`.
- Exports are prepared while the request runs, written to a temporary file as the responses are read in batches of 500, and then sent; the dialog shows "Preparing…", then "Ready" with Download. A background job (with the public API's asynchronous export) follows when exports outgrow a request. Owners, admins, editors and analysts export; every export is in the audit log.
- System.Security.Cryptography.Pkcs is lifted to 10.0.13 by transitive pinning: PDFsharp 6.2.4 asks for 8.0.1, which has known vulnerabilities.
Work item: AWK-24.

## 2026-10-14 — Domains, publishing and invitations (AWK-7, W12)
Owner decisions: W12 approved; short links yes; an unsubscribe link in every invitation yes.
- Survey links are `/s/{code}`: eight characters from an alphabet without look-alikes (`ShortCode`), unique across the instance; existing surveys got one in the migration, and `/s/{id}` keeps working. The runner finds a published survey by its code through `run_survey_id`.
- Domains: one screen in the organisation menu (owners and admins). A domain is verified by a CNAME to the instance's address and a TXT record `_askwerk.{host}` = `askwerk-verify={token}`; Verify checks now, and `DomainChecks` checks pending domains every five minutes for seven days, then marks them failed. DNS is read by our own small client (`Infrastructure/Dns`, RFC 1035 over UDP to the machine's servers), no library. Caddy's on-demand TLS asks `GET /api/caddy/ask?domain=`; yes for the system address and verified domains only. Organisation subdomains of a wildcard base domain (architecture.md) are not built; the system address is the instance's base address.
- Publish shows the link on every verified domain, primary first, with one address switch; link, QR code (PNG/SVG, our encoder), embed code and PDF (`?host=`) follow it.
- Embedding: `/s/embed.js` (Apache-2.0) frames the survey, keeps it as tall as the survey and passes on `askwerk:started|page|completed|resize`, never answers (`docs/api/embed.md`). Embedding is off until an owner or admin lists websites at the bottom of Domains; survey pages send `Content-Security-Policy: frame-ancestors 'self' <listed origins>`, every other page `frame-ancestors 'self'`. Over HTTPS the runner's resume cookie is `SameSite=None; Partitioned`, so it works in a third-party frame.
- Jobs: the job table and `JobRunner` (every 15 seconds, `claim_jobs` with FOR UPDATE SKIP LOCKED, five tries). `ASKWERK_JOBS=manual` leaves running to the caller; the tests use it.
- Invitations: people pasted (address or `Name <address>`) or from CSV/Excel with a header row (email, name, language, in English or German); our own small Excel reader. Each person gets the organisation's invitation text (`MessageComposer`, shared with the preview and test send) in their language, their own link `/s/{code}?t={token}` and an unsubscribe link `/s/u/{token}`. One reminder three days before the deadline to those who have not answered; "Send reminder now" on Results → Participants. A refused address is marked bounced and not tried again. Without SMTP, personal links are made instead and downloaded as CSV.
- The participants list is a third tab on Results (Overview | Responses | Participants), since the designer has no lasting publish screen; the published view opens the invitation.
- The designer is a desktop tool: the publish and invitation browser tests run on desktop only, like the other designer flows.
Work item: AWK-7.

## 2026-10-14 — The shared data grid (AWK-19, W13)
Owner approved W13 with three answers: bulk selection on Participants now, page numbers in the pager, responses sorted by time only. No hi-fi mockup.
- One `DataGrid` in `packages/ui` for every list: sortable headers (the server sorts, so every page is covered), numbers, durations and dates aligned right in Plex Mono, rows 40 px, skeleton rows while loading, empty and error states, a card per row on phones with one Sort menu. Styles in `tokens.css`; `/dev/controls` shows it in its states.
- Paging is the default: 25, 50 (default) or 100 per page, page numbers with gaps. The survey list keeps page, sort and size in the address and the size on the device; going back from a survey returns to the same place. "Show more" is gone.
- The response list loads 50 more when its end comes into view (or with "Load more"). From 200 loaded rows on, only the rows near the screen stay in the page, so long lists stay smooth. It sorts by start time only, newest or oldest first (`order=oldest` on the same cursor); sorting by an answer column over 1 M responses would not meet the 200 ms read budget.
- Selection only where a screen does something with several rows. Participants: Send reminder and Remove for the chosen ones, or for every participant with "Select all N". Removal asks first; people who started or sent a response are kept, so a response never loses its person. The app role may now delete participants (migration `ParticipantRemoval`). Both actions are in the audit log.
- App API: the survey list takes `sort`, `limit` and returns `total` instead of `hasMore`; participants take `sort`, `offset`, `limit` and return `total`; `POST .../participants/remind` takes chosen ids; `POST .../participants/remove` is new. None of this is public.
Alternatives rejected:
- A grid library: sorting, paging and a window of rows are a small amount of our own code, and a library would bring its own look.
- Sorting responses by any column: not fast enough at the target size.
Work item: AWK-19.

## 2026-10-15 — Background exports and the first Public API (AWK-25, W14)
Owner approved W14 (both parts) with the recommendations: export files kept 7 days, every export as a job, key presets Read results and Full access, the five endpoints public, SDKs in a follow-up item (AWK-26), problem types under https://askwerk.org/problems/. No hi-fi mockup.
- Exports: one path for the designer and the Public API. `ExportService` asks for an export (`export` table, row-level security), `PrepareExportJob` writes the file in batches of 500 with progress into the organisation's file storage (`{tenant}/{export}-export`), and `DeleteExportJob` removes it after 7 days. A job that gives up shows as `failed` (`export-failed`); Excel above one sheet fails at once. At most three exports of an organisation run at the same time (`429 too-many-exports`). The export endpoints nudge the job runner, so a small export is ready within a second instead of at the next 15-second round. The synchronous App API export is gone.
- API keys (`api_key`, row-level security): `awk_` plus 44 random characters, shown once; only the SHA-256 is kept (enough for a random secret of that length; no slow hash needed). The first 12 characters find the key through `api_key_lookup`, a SECURITY DEFINER function, before the organisation is known; the secret is compared in constant time. Scopes are those of `public-api.md` without `settings:*`. Owners and admins create and revoke keys; every change is audited. Last use is written at most once a minute.
- Authentication: a selecting scheme picks the key handler for /api/v1 and the cookie session everywhere else, so cookies never reach the Public API and keys never reach the App API. The key handler switches the request into the key's organisation and to no user.
- Public API v1: `GET /surveys` (cursor), `GET /surveys/{id}`, `POST /surveys/{id}/exports` (`Idempotency-Key` per key for 24 hours), `GET /exports/{id}`, `GET /exports/{id}/file`. Errors are problem details with `https://askwerk.org/problems/{code}` and titles in German and English (`docs/api/errors.md`). Rate limit: our own fixed one-minute window per key (600 by default, `ASKWERK_API_RATE_LIMIT`) with `RateLimit-*` headers; it counts per instance.
Alternatives rejected:
- ASP.NET Core's rate limiter for the Public API: it does not give the remaining calls for the `RateLimit-Remaining` header the contract promises.
- A slow password hash for keys: needless for 44 random characters, and it would slow every call.
- Exports above a size threshold as jobs and small ones in the request: two paths to test and to explain; the nudge makes small jobs as quick.
Work item: AWK-25.

## 2026-10-15 — Analysts and viewers read Results, with Preview and the PDF (AWK-27, W15 rev. 2)
Owner approved W15 rev. 2, which supersedes W15 (a read-only designer). A review is done with Preview and the PDF; the variable names and codes are in every export's codebook; question settings and logic rules stay in the editor, for editors.
- Owners, admins and editors open a survey in the editor; analysts and viewers open its Results (a link to the editor leads there too) and get no Edit tab. Before publishing, Results points them to Preview.
- Preview (side panel, new tab, PDF) and Download PDF are in the Results bar for everyone.
- The draft preview (`GET .../preview`, `PUT .../preview/pages/{page}`) is open to every member of the organisation; it still stores nothing. This supersedes "only for editors" in the entry "Survey settings and preview as built (AWK-22)".
- A recorded approval of a version (ISO 20252) is AWK-28, wireframe first.
Alternatives rejected:
- A read-only designer: much to build for what Preview, the PDF and the codebook already show.
Work item: AWK-27.

## 2026-10-15 — One bar and tabs for every screen of a survey, and pages edited in place (AWK-29, W17)
Owner approved W17 with its recommendations: Settings and Share as tabs, Participants its own tab, Share as the primary action once published.
- Every screen of a survey has the same bar: Surveys (back), the name with the survey's menu, the status, Search (editor only), the language, Preview and one primary action: Publish for a draft, Publish changes after edits, Share once published. Readers get no primary action.
- Tabs: Build, Settings, Share, Results, Participants, with the response and participant counts. Analysts and viewers see Results and Participants only. Routes: `/surveys/{id}` (Build and Settings), `/share`, `/results`, `/participants`.
- The survey's menu: Rename, Duplicate (a copy of the draft and theme, "Copy of …"), Save as template (in the editor), Download PDF, Close for responses… / Reopen for responses, Archive…. Readers get Download PDF only. Archived surveys leave the list unless filtered by Archived; their results stay.
- Settings replaces the outline's "Survey settings" entry; the outline stays beside it.
- Pages: selected from the outline or the canvas; the title is edited on the canvas; the inspector shows the page (position, questions, translations, move, delete). The page menu moves, duplicates and deletes; the last page cannot be deleted; deleting asks with the number of questions.
- App API: `GET .../summary`, `PUT .../title`, `POST .../duplicate`, `POST .../close`, `.../reopen`, `.../archive`, `GET .../links`; each change is audited (survey.renamed, .duplicated, .closed, .reopened, .archived).
Alternatives rejected:
- Edit | Results as a switch with Share inside the publish dialog: Share and Participants were hard to find again after publishing.
Work item: AWK-29.

## 2026-10-15 — Approve a survey from Preview, recorded (AWK-28, W16)
Owner approved W16 with its recommendations: every member may approve; publishing does not require an approval; no rule against approving one's own changes.
- Approve… sits in the Preview side panel's header, from the editor and from Results and the other tabs. One step: the survey's name and languages, a note that a later change is not covered, an optional note (at most 500 characters).
- An approval is pinned to the saved draft's fingerprint (SHA-256 of its definition). After a change it counts for an earlier draft only. Only its author withdraws it.
- Editors see the state next to the primary action: "✓ Approved · name, time", "! Approved an earlier draft" or "Not approved"; the pill lists every approval with its note. The Publish dialog says it in one line; publishing never waits.
- Audit: survey.approved and survey.approval-withdrawn with the draft number, fingerprint and note; survey.published records the fingerprint and whether that draft was approved.
- App API: `GET`, `POST .../approvals`, `DELETE .../approvals/{approvalId}`; table survey_approval with row-level security.
Alternatives rejected:
- An organisation setting "Publish only approved drafts": not needed now; it can come later without changing the data.
Work item: AWK-28.

## 2026-10-15 — Members of an organisation (AWK-30, W18)
Owner approved W18 with its recommendations: invitation links valid 7 days; admins invite and change every role except owner; when a host system manages members (AWK-12), the page will only show them.
- Organisation → Members for owners and admins: the shared grid with the role as a dropdown in the row (one's own row and, for admins, owners' rows read-only), two-factor and last sign-in; Remove… asks first. The roles are explained folded away. Open invitations sit under the list with Copy link, Send again (with email) and Revoke.
- Invite in one step: addresses one per line, checked as typed (new, already a member, not an address), one role for all, Editor preselected. With email set up the links go out; without, the links are there to copy. One open invitation per address; a new one replaces the old.
- The link (/join/{token}) works once, for 7 days. A new person sets name and password (the address comes from the invitation); an account signed in joins as it is; an address with an account signs in first, then joins. Owners and admins then set up two-factor, as at every sign-in.
- Rules: there is always an owner (the last owner can be neither changed nor removed, nor leave); only owners manage owners; nobody changes their own role. Removal ends the person's sessions at once (a new security stamp signs them out everywhere). Anyone but the last owner may leave from the organisation menu.
- Audit: member.invited, member.joined, member.role-changed, member.removed, member.left, invitation.revoked; user.created for a new account.
- Storage: member_invitation with row-level security; only the token's SHA-256 is looked up, through the definer function member_invitation_tenant, which answers with the organisation and nothing else; the token itself is kept encrypted with the instance's keys so the link can be copied again. app_user.last_sign_in_at is set at every sign-in.
- The invitation email is a plain account email, like the password reset, in the instance's language; it does not use the organisation's survey look (that look belongs to survey invitations).
Alternatives rejected:
- Storing only the hash: Copy link would then work only right after inviting.
- Ending only the sessions in that organisation: a session carries one organisation, and a new stamp is the one sure way; the person signs in again for their other organisations.
Work item: AWK-30.

## 2026-10-15 — Survey logic: show, skip and end (AWK-31, W19)
Owner approved W19 with its recommendations: only show, skip and end now (piping and quotas later, in their own items); one level of all/any; ending shows the normal thank-you page.
- Scope correction: the definition and the runner did not know logic yet. AWK-31 adds it to the schema, the runner, the server and the designer.
- Definition: `showWhen` on questions and pages ({ match: all | any, conditions }), `after` on pages ({ when?, goTo? | end }). Conditions: is, is-not, is-one-of, includes, includes-any, includes-none, at-least, at-most, between, answered, not-answered, on option codes or numbers. Conditions use earlier questions only; jumps go only forward, so loops cannot happen.
- One evaluator in TypeScript (`route` in packages/schema) and its C# twin (`SurveyLogic`), both run the golden corpus `tests/corpus/logic`. Answers to questions that are not shown count as not given, so a hidden question never steers the way.
- Runner: questions appear as earlier answers ask for them, skipped pages are left out, an ending makes its page send the survey; only shown answers are sent.
- Server: a page save checks and stores only the questions shown with the answers so far and remembers the next page on the way; completing requires only the questions on the way.
- Publish checks: logic-question-missing, logic-question-later, logic-option-missing, logic-page-missing, logic-jump-backward (errors), in both check implementations and the corpus.
- Designer: "Show this question/page: Always / Only when …" with a builder of earlier questions, read back as a sentence; "After this page: next / jump under a condition / end"; a badge on the card and a mark in the outline; Settings lists every rule with Edit. Deleting a question used in a rule asks and removes those conditions; moves that would break a rule are refused with the reason.
- PDF: every rule printed in words ("Answer only when …", "This page only when …", "The survey ends here when …").
Alternatives rejected:
- Nested condition groups: rarely needed, much harder to read.
- An expression language: sentences built from the survey's own questions are enough for show, skip and end.
Work item: AWK-31.

## 2026-10-15 — Public API follow-ups, step 1: webhooks (AWK-32, C32)
Owner approved C32 with its recommendations: opaque access tokens (step 2), an own small MCP handler (step 3), four events now, https-only webhook URLs, and building and merging in three steps (webhooks, OAuth, MCP).
- Public: GET/POST /api/v1/webhooks, PATCH/DELETE /api/v1/webhooks/{id}, GET .../deliveries (cursor), POST .../deliveries/{deliveryId}/replay, scope webhooks:manage. The API page (owners and admins) uses the same service through /api/app/webhooks and adds a ping.
- Events now: response.completed, export.completed, survey.published, survey.closed (and ping). Each is queued as a delivery and a job in the same unit of work as the change (the job table is the outbox).
- Delivery: POST with Askwerk-Event, Askwerk-Delivery and Askwerk-Signature (t, v1 = HMAC-SHA256 over "t.body"); 10 seconds to answer; retries after 1 min, 5 min, 30 min, 2 h, 6 h and 12 h; at-least-once with a stable event id; a webhook failing for 3 days is paused until resumed.
- Storage: webhook (secret encrypted with the instance's keys, so it can sign) and webhook_delivery, both with row-level security. At most 20 webhooks per organisation.
- Docs: docs/api/webhooks.md with the signature check in Node, C# and Python.
Work item: AWK-32.

## 2026-10-15 — Public API follow-ups, step 2: OAuth client credentials (AWK-32, C32 B)
- Clients are created on the API page (owners and admins) with the key presets; the client id (cl_…) is public, the secret (acs_…) is shown once and stored as its SHA-256. Clients expire after one year.
- POST /api/v1/oauth/token (application/x-www-form-urlencoded, credentials in the body or as HTTP Basic, optional narrower scope) gives an opaque access token (awt_…) for 15 minutes, stored as its hash; no refresh tokens. Errors follow RFC 6749.
- The Public API's bearer scheme accepts keys and tokens; scope policies require either. A token is found through the definer function access_token_lookup, which ignores tokens of revoked or expired clients, so revoking ends them at once. The token endpoint finds its client through oauth_client_lookup.
- Calls with a token are audited with the client as actor (AuditActorKind.OAuthClient).
- No new package: JWTs were not needed (owner decision, C32 (a)).
Work item: AWK-32.

## 2026-10-15 — Public API follow-ups, step 3: the MCP server (AWK-32, C32 C)
- POST /mcp, Model Context Protocol over Streamable HTTP, one JSON-RPC 2.0 message per request; protocol versions 2025-06-18, 2025-03-26 and 2024-11-05; methods initialize, ping, tools/list, tools/call; notifications get 202; GET gives 405 (no server-initiated messages).
- Authenticated like the Public API (key or OAuth token, the "integration" policy); each tool checks its scope and calls the Public API's own controller action, so it answers the same JSON and can never do more. Refusals are tool results with isError.
- Tools now: list_surveys, get_survey (surveys:read), start_export, get_export (responses:read). New tools come with new public endpoints.
- Own small handler, no package (owner decision, C32 (b)). Not in the OpenAPI document; documented in docs/api/mcp.md with Claude Desktop, VS Code and curl.
Work item: AWK-32.

## 2026-10-15 — The rest of the shared controls (AWK-19, W20)
Owner approved W20 with its recommendations: move every screen in one pass; dialogs open as bottom sheets on phones.
- packages/ui now has Dialog (with DialogActions), ConfirmDialog, Menu (MenuItem, MenuLink, MenuSeparator), Tabs, Switch, Checkbox, RadioGroup, Field and Notice, styled once in tokens.css (aw-dialog, aw-menu, aw-tabs, aw-switch, aw-check, aw-formfield, aw-notice). Dialogs keep the focus inside and give it back; menus and tabs work with the keyboard.
- Moved: every dialog except the command palette (a combobox in its own panel) and the publish dialog's three-part frame (approved in M3; it now sits on the shared backdrop); the survey, page and Preview menus; the tabs of Results, the inspector, the embed code and the API page; the inspector's switches and radio groups.
- Not moved: the organisation and account menus in the app bar (radio items with a different layout) keep their own markup on the same look.
Work item: AWK-19.

## 2026-10-15 — Responses since a sequence, codebook, SQL Server example (AWK-14, C14)
Owner approved C14 with its recommendations: the two read endpoints are public in v1; the example uses Microsoft.Data.SqlClient; externalId stays empty until host users exist (AWK-12).
- GET /api/v1/surveys/{id}/responses?since&limit&language (responses:read): ordered by response.change_seq; nextSince is the last sequence of the page. Long format with the export codebook's variables (multiple choice as QUESTION_CODE = 1, unchosen options left out), so the API, exports and the example name variables alike.
- GET /api/v1/surveys/{id}/codebook?language (responses:read): the latest published version; 409 not-published otherwise.
- docs/integrations/sql-server-sync (Apache-2.0): a console job, one transaction per page, sync state per survey. Its Sync.cs is compiled into Askwerk.Tests.Api and tested against SQL Server 2022 (Developer edition) in a container, in the full run only (ASKWERK_TEST_SQLSERVER=1, set by bin/test-all.sh --full).
- Packages: Microsoft.Data.SqlClient 7.1.1 (MIT, Microsoft) for the example and that test only; the example pins System.Security.Cryptography.Pkcs 10.0.13 because SqlClient asks for a vulnerable 9.0.18. Never a dependency of Askwerk itself.
Work item: AWK-14.

## 2026-10-15 — SDKs for the Public API (AWK-26, C26)
Owner approved C26 with its recommendations: our own generator, no new package; package names @askwerk/sdk, Askwerk.Sdk, askwerk; publishing stays manual on the owner's word.
- src/sdks/generate.mjs (Node, no package) writes the typed part from docs/api/openapi.json: models and one class per resource (surveys, responses, exports, webhooks). Operation names are listed in the generator; a new endpoint fails it until named. Cursor- and sequence-paged lists get an "all" variant. SdkTests.TheGeneratedCode_IsCurrent runs `generate.mjs --check` in every run.
- Hand-written per language: transport (bearer from an API key or OAuth client credentials with renewal, one error type with the problem details, up to three retries on 429 after Retry-After, an Idempotency-Key on export starts), waiting for exports, webhook signature checks. Standard library only: fetch, HttpClient, urllib.
- Targets: Node 20+ (ES2022), .NET 8+ (the C# SDK is in the solution and builds under Askwerk's analyzers), Python 3.11+.
- Tests: SdkTests runs one scenario in C# (in process), TypeScript (node, type stripping) and Python against a second app on a real port over the same database, with jobs running by themselves; full run only (ASKWERK_TEST_SDKS=1).
- src/sdks/release.sh builds the packages into artifacts/sdks/ (npm pack, dotnet pack, a stdlib wheel builder); it never publishes.
Work item: AWK-26.

## 2026-10-15 — Identity integration step 1: hand-over and provisioning (AWK-12, W22)
Owner approved W22 with its recommendations: three steps (hand-over and provisioning, then OIDC, then LDAP); owners always keep an Askwerk password; users matched by email on their first external sign-in.
- Hand-over: GET or POST /handover with a JWT checked by .NET's own crypto (no package): RS256 or ES256, iss, aud = askwerk:{organisation}, sub, email, exp at most 5 minutes ahead (30 s skew), jti once (handover_token_use, cleaned as it expires). aud names the organisation, so its settings are read inside its row-level security; no definer function. Keys: up to two PEM keys and/or an https JWKS URL cached 10 minutes.
- A host user is bound by (organisation, external id) in external_identity; the first time matched by email, else created with UserSource.Provisioned and no password. Roles from "Roles from your system" (first matching group; owners never mapped; an owner's role never changed); "anyone else" may be "not let in". Host sessions skip Askwerk's two-factor set-up: the host answers for it.
- Provisioning API /api/v1/users (PUT, GET, DELETE) with the new scope users:manage, which is never in a preset (W14 said keys never manage users; W22 makes this one deliberate exception). DELETE removes the membership and ends sessions; the user and their responses stay.
- Members page: members with an active external identity show "Managed by {system}"; role change and removal answer 409 managed-externally.
- The SDK generator names the three new operations (users.list, put, deactivate).
Work item: AWK-12.

## 2026-10-15 — Identity integration step 2: single sign-on with OpenID Connect (AWK-12, W22)
- Package Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.12 (MIT, Microsoft; owner approved). It brings Microsoft.IdentityModel.* 8.19.2 and System.IdentityModel.Tokens.Jwt (MIT, Microsoft), used only by this handler; the hand-over keeps its own check.
- One scheme per organisation (oidc-{id}), added on first use or when its callback (/sso/callback/{id}) arrives after a restart; OidcOptionsSetup reads the settings from the database inside the organisation's row-level security; saving the settings forgets the cached options. Code flow with PKCE, client secret protected at rest, correlation and nonce cookies Secure on https.
- /sso/{slug} starts it; a definer function oidc_tenant_by_slug finds only organisations with single sign-on on. The sign-in page has "Sign in with single sign-on" (asks for the short name), folded away.
- OnTicketReceived binds external id "oidc|{sub}" through the same ExternalUsers as the hand-over (matched by email once, roles from the same rules, owners untouched), refuses email_verified false and deactivated users, starts an external session. Failures show the same small page as the hand-over.
- Tests: an in-memory provider (discovery, JWKS, token endpoint) behind the handler's back channel; the test plays the browser at the authorize endpoint.
Work item: AWK-12.

## 2026-10-15 — Embedding the designer and Results (AWK-13, W21)
Owner approved W21 with its recommendations: embedded sessions as in-memory bearer tokens (no third-party cookies); the survey list stays out of the frame; per-survey narrowing of the runner's websites.
- /embed/designer and /embed/results (the designer app): the page's frame-ancestors are the organisation's designer websites (tenant.designer_embed_origins, read through the definer function designer_embed_origins from the token's aud), 'none' without a valid organisation. The token leaves the address at once.
- POST /api/app/embed/session checks the hand-over token (claim survey required, jti once), applies the user and role like the hand-over, and returns an aws_ token (SHA-256 kept in embed_session; 1 hour, renewed by POST …/renew, 8 hours at most). The selecting scheme sends "Bearer aws_" to the EmbedSession handler (found through embed_session_lookup). UseEmbedLimits keeps such a session to /api/app/me, its own survey (surveys/{id}, run/{id}) and the shared reads (themes, templates, assets, messages, exports, status, telemetry); everything else is 403. Analysts and viewers get Results.
- The frame posts askwerk:ready, saved, published, navigate, resize, session-ending and error to the host (target "*", as the runner; nothing secret is in them).
- Runner narrowing: survey.embed_origins (a subset of the organisation's), used by run_embed_origins when set; Share → Embed → "Only on these sites" under the embed code.
- Identified respondents: the runner's start takes identity (a hand-over token with the survey claim, used once); the response keeps respondent_external_id and respondent_email, shown in the responses API.
Work item: AWK-13.

## 2026-10-15 — Identity integration step 3: the directory over LDAPS (AWK-12, W22)
Owner chose our own small managed LDAP client over System.DirectoryServices.Protocols, because the chiseled runtime image has no native libldap; the image stays unchanged and no package is added.
- Askwerk.Api/Identity/Ldap.cs: LDAP v3 over SslStream (LDAPS only), BER for simple bind, one subtree search and unbind; filters are built as BER (an AND of equality matches), never parsed from text, so a user name cannot change them. An empty password is refused before any bind (it would be an anonymous bind). Certificates chain to the system's roots or to an optional CA (PEM) per organisation.
- DirectoryPasswords: the search account finds exactly one entry (objectClass and the sign-in attribute), its DN binds with the password; mail is required; id entryUUID, objectGUID or the DN; groups from the group attribute plus the CN of each DN. Five wrong passwords per organisation and user name pause it for 15 minutes (in memory).
- Settings on Organisation → Sign-in: Active Directory or other LDAP preset, server, search account, base DN; port, attributes, CA and name folded under More settings; "Test connection" with a test person (POST /api/app/sign-in/ldap-test, nothing stored).
- Sign-in: GET /api/app/session/sso/{organisation} (definer function sso_options) says single sign-on, directory or both; POST /api/app/session/ldap signs in through the same ExternalUsers (external id "ldap|{id}", matched by email once, same role rules, owners untouched). /sso/{organisation} sends a directory-only organisation to /?sso=… with the directory's form.
- Tests: Askwerk.Tests.Api/Ldap/Dockerfile builds OpenLDAP from Alpine's own packages with a test CA made at build time (no third-party LDAP image); DirectorySignInTests runs against it in the full run (ASKWERK_TEST_LDAP=1); the unreachable-directory and options test runs every time.
Work item: AWK-12.

## 2026-10-15 — Places named for what they hold (AWK-29, W23 round three)
Owner rejected the first two rounds (moving the same buttons) and approved round three in the 44 px hi-fi: keep today's two lines and their look; distinguish where is what.
- Row 1 (this survey): the logo and Surveys, the name, the status, ⚙ Survey settings, search, language, Preview, approval, the primary action, a divider and the profile menu (ProfileMenu, the app bar's account menu on the shared Menu). Clicking the name renames it in place (Enter saves, Escape keeps); there is no menu behind the name any more.
- Row 2 (its places): three groups with a small label above their tabs, 44 px: Create (Questions = build, Look = settings), Collect (Share, Participants), Analyse (Results). Readers see Collect and Analyse only. Routes: /surveys/{id} Questions, /surveys/{id}/look, …/survey-settings.
- Look (was the Settings tab): theme, header image and layout in the inspector; the canvas shows the runner preview of the saved draft, loaded again after each save. The welcome and thank-you pages moved to the page list (first and last, each with its own small inspector), all rules to its foot ("All rules"), the languages line to Survey settings.
- ⚙ Survey settings (SurveySettingsPage): name, languages (read-only), collecting (close, reopen), and duplicate, save as template, archive. Download PDF is only under Preview now. Closing dates and one response per person, named in the round-three table, do not exist yet and are not on the page.
- Page menu: the ⋯ menu is gone. Pages move by dragging or Alt+↑/↓ (focus stays on the page), and with Move up/down in the page inspector; Duplicate and Delete sit beside the selected page's title on the canvas.
Work item: AWK-29.

## 2026-10-15 — Closing date and one response per person (AWK-29, W24)
Owner approved W24 as drawn: both settings in Survey settings → Collecting responses; defaults no closing date and one response per person on.
- Closing date: survey.closes_at (UTC). PUT /api/app/surveys/{id}/closing-date takes a local date and time (2026-10-31T18:00) in the instance's time zone (instance settings; organisations have no time zone of their own yet) and converts it with TimeZoneInfo; a past moment is 400 closing-date-past, an hour the clocks skip 400 closing-date-invalid; null removes it. Draft and published surveys only.
- From that moment Survey.StatusAt reports closed: the runner refuses new starts and saving or finishing unfinished responses (409 closed, which also applies to closing by hand now), and the summary and list say closed. A close-survey job, enqueued for the date, sets the status, writes survey.closed as the system with reason closing-date and raises the survey.closed webhook; a date moved later leaves the old job with nothing to do. Reopening removes a closing date that has passed; closing by hand keeps a future one.
- One response per person: survey.one_response_per_person, default true (also for existing surveys, the migration's default). It is per browser: the runner's resume cookie, scoped to the survey, now lives a year once the response is complete, and the start is refused with 409 already-answered while it points to a complete response. Off, the end page offers Answer again, which starts a new response. Another browser or a private window can answer again; strict one per person stays with personal invitation links, which already allow one.
- Audit: survey.closing-date-set and survey.closing-date-removed (before, after, time zone), survey.one-response-changed (on). Both columns are on survey, so its row-level security policy (survey_tenant) covers them; no new table, no new dependency (the browser's datetime-local field and Intl). The runtime image gets the build stage's /usr/share/zoneinfo (Ubuntu tzdata, public domain): the chiseled image has none, and converting from the IANA zone failed there. It is OS data, not a package; removing it means converting in the browser instead.
- Rejected: evaluating the date only lazily (the list, Results and webhooks would never see the survey close) and a fingerprint of the respondent's device (personal data, and still not reliable).
Work item: AWK-29.

## 2026-10-15 — Settings into row 2, groups on tiles (AWK-29, W25 C)
Owner approved W25 version C after two rounds (labels only; one faint icon per group; a duotone icon on every tab; smaller labels).
- ⚙ Settings (the survey's settings page) is no longer a button in row 1 but a tab at the right end of row 2, after a divider, for everyone (read-only for readers). Row 1 is the survey and what to do now: name, status, search, language, Preview, approval, primary action, profile.
- Each group (Create, Collect, Analyse) sits on a soft tile (6 % ink over the paper colour, so it follows light and dark themes) with one strong icon at its start: a pencil, a tray, a chart on an ink square. The visible labels are gone; each group keeps its name for screen readers (role group, aria-label). Row 2 is 48 px.
Work item: AWK-29.

## 2026-10-15 — One Preview; Look is the survey's look definition (W26)
Reason: owner decision; Look and Preview both showed the survey. Preview (side panel, new tab, PDF, Approve) is the one place to see it; in Look it is docked beside the settings and takes the look being edited live. Look opens on a gallery of 16 looks, then "Make it yours" with every setting in seven folded areas. A survey has its own look (tokens on the survey, with the look it started from); its theme or the organisation's default applies otherwise. New tokens are optional, so existing themes stay valid; email and PDF read the same tokens. Eight sample surveys (each with its look, drawn pictures and sample answers, created closed) show the app. Alternatives rejected: per-survey copies as organisation themes (clutters the theme list); a live runner per gallery card (16 iframes). Work item: AWK-33.

## 2026-10-15 — Developers hub beside Surveys (W27)
Reason: owner decision; developers need docs, samples and a way to try the public API without the Organisation menu. "</> Developers" sits in the top bar for every member; guides are the documents in docs/, bundled with the app so they match the version; the API reference reads the live OpenAPI description; Try it calls this Askwerk from the browser with a key kept in the tab only, and asks before changes. Owners and admins get a one-hour read-only trial key (surveys:read, responses:read). The Postman collection is generated per download from the committed OpenAPI description with the instance's base URL. Alternatives rejected: a third-party API console (a dependency, and it would load from outside); a static collection in the repository (goes stale, wrong address). Work item: AWK-34.
## 2026-10-15 — Drawn backgrounds are built in, not uploaded (W28)
Reason: owner decision (W28 approved). The Panorama look ships its mountain landscape as part of Askwerk: SVG on the web, the same scene drawn by SkiaSharp for the PDF band, so the look works in every organisation without a file. New optional tokens: pages.questions.panel (solid, frosted), pages.title (panel, over-picture), components.textBlock.style (tinted, plain), pattern "mountains". Frosted panels fall back to solid with prefers-reduced-transparency. Alternatives rejected: a seeded image asset per organisation (a look would then depend on a file). Work item: AWK-35.

## 2026-10-16 — The app in a sidebar; one Administration; an installation level (W29)
Reason: owner decision after six rounds. Every page sits in a white sidebar: the organisation box on top (switching always opens that organisation's survey list), Surveys and Templates, then one Administration group (Organisation, Members, Sign-in, Look of surveys, Messages, Domains, Developers & API) and, for installation administrators only, "This Askwerk" (Organisations, Administrators, Installation). Inside a survey the sidebar steps in (drill-in): "All surveys" as an ordinary row, the survey's name and facts, its places; the survey bar becomes one row. Installation administrators are a flag on the user (whoever ran setup, then those they add); organisations can be created (owner by account or invitation), suspended, resumed and deleted (closed, data kept). Suspended and deleted organisations close sessions (as if none chosen), API keys and tokens, and survey links. On phones the sidebar is a drawer. Alternatives rejected: top tabs, an organisation rail, a home with a launcher, a separate installation area outside Administration. Work item: AWK-38.

## 2026-10-16 — Deleted organisations are removed after 30 days; installation settings in the app (W30)
Reason: owner decision. A deleted organisation is closed at once and kept 30 days; installation administrators can restore it, or remove its data at once after typing its short name. After 30 days an hourly check removes it. Removal deletes every row carrying its tenant_id except the audit log, and its files; the organisation's row stays, marked removed, with its short name freed, so the audit log keeps its name and who removed it. The installation's name, language for new organisations, time zone and email (with a test email) are edited on Administration → Installation. Values set in the configuration (ASKWERK_NAME, ASKWERK_LANGUAGE, ASKWERK_TIME_ZONE, ASKWERK_SMTP_*) win and show as locked, so whoever runs the server keeps control; the address stays read-only because links depend on it. Alternatives rejected: keeping deleted data until removed by hand, removing at once without a grace period, per-organisation time zones (one installation serves one region for now). Work item: AWK-39.

## 2026-10-16 — One squashed commit per finished feature, pushed to main
Reason: owner decision, while Askwerk is a one-person project; fewer commits and no GitHub-side merge traces (PR pages, merge co-author lines). Atomic commits stay local on the feature branch; after a green full run the branch is squashed locally into one commit on main and pushed. Alternatives rejected: squash-merging pull requests on GitHub (adds a Co-authored-by line from the account email and leaves PR pages, with no reviewer or CI to justify them). Outside contributions still come as pull requests. Work item: AWK-40.
