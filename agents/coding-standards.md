# coding-standards.md — how code is written

## Licensing rule (applies to everything)
Askwerk is open source: AGPL-3.0 for the product, Apache-2.0 for the integration surface. The exact folder scope and the list of compatible dependency licences are in `decisions.md` (licence entries); that list is the only one. In short: dependencies of AGPL code must be AGPL-compatible; dependencies of the Apache-2.0 folders must be permissive only, because integrators inherit them. Every source file carries an `SPDX-License-Identifier` header; CI enforces it. Commits are signed off (DCO). No commercial, open-core-with-paid-tiers-we-rely-on, "free for non-commercial" or source-available-with-restrictions packages. Known exclusions: SurveyJS Creator/Dashboard/PDF, MediatR and AutoMapper (commercial since 2025), FluentAssertions 8+ (commercial since 2025), Hangfire Pro, Telerik/DevExpress/Syncfusion, MUI X Pro, AG Grid Enterprise.

## Dependencies (no hard dependencies)
- The default install is the Askwerk image plus PostgreSQL; Caddy is optional. A feature never adds a service or container (identity broker, object store, cache, queue, search engine, mail server) to that install. Optional providers are fine when the GUI switches them on and Askwerk works without them.
- Prefer, in this order: what .NET or the browser already provides; a small implementation of our own; an open-source library.
- No open-core libraries whose paid tier we would come to need, no libraries that dictate the architecture. Native code (e.g. SkiaSharp's native assets) is allowed; what rules a library out is that we do not need it or that its licence is not free (owner, 2026-10-14).
- **Pin a library to its last free version when a later one changes the licence.** PDFsharp/MigraDoc: exactly 6.2.4, the last MIT version; never 6.5 or later (AGPL). The pin is exact in `Directory.Packages.props` with a comment, and a test fails on any other version.
- A third-party library that is used in more than one place sits behind our own interface or wrapper, so it can be removed in one place.
- **Warn the owner before adding a dependency** (owner rule, 2026-10-14): name, version, licence, maintainer, date of the last release, and what it is for, in chat, before it is added. Say plainly when it is not OSI open source or not actively maintained; such a dependency waits for the owner's yes.
- Every new dependency (library, service, SaaS or tool) gets a `decisions.md` entry with: licence; why we cannot write it ourselves in reasonable time; which interface or wrapper it sits behind; how we would remove it.
- Approved today:
  - Backend runtime: EF Core + Npgsql, MailKit (behind `IEmailProvider`), OpenTelemetry .NET (SDK, OTLP exporter, ASP.NET Core / HTTP / EF Core instrumentation; development-only in-memory exporter for tests), Microsoft's JWT bearer package for API client-credentials tokens (`public-api.md`).
  - Backend tests (development only): xUnit, Testcontainers. Snapshot checks (OpenAPI documents) use a small helper of our own, not Verify (decisions.md, 2026-10-13).
  - Frontend runtime: React, TanStack Router, TanStack Query, Radix UI (wrapped in `packages/ui`), dnd-kit (wrapped in `packages/ui`), zod.
  - Frontend build: TypeScript, Vite, pnpm, ESLint, Prettier; Tailwind CSS v4 (build time only) once the first real screens need it, until then plain CSS on the `packages/ui` tokens.
  - Frontend tests (development only, never shipped): Vitest (runs on Vite, so no second build pipeline), Testing Library (tests components the way a user finds them, by role and label), Playwright (real browsers for flows on phone and desktop), axe-core (accessibility), Lighthouse CI (runner budget). Writing these ourselves is not reasonable; each is MIT or Apache-2.0 and replaceable without touching product code.
- No mocking or assertion libraries; fakes are hand-written (`testing.md`).
- Not used, by decision: Hangfire, Quartz.NET (documented fallback only), MediatR, AutoMapper, Mapperly, FluentValidation, FluentAssertions, Serilog, ReadStat, Keycloak, Authentik, MinIO, Redis, motion, Zustand, Redux, react-hook-form, Recharts, visx, Storybook, Lingui, i18next, the Lucide npm package.
- Still open, decided in the work item that first needs them (with a `decisions.md` entry): load-test tool (NBomber or a small script of our own), Excel read/write for import/export, QR code generation, PDF output (print CSS first).

## Readability first (all languages)
- Code is written for the human reading it next. Prefer the obvious version over the clever one; a reader should not need to know a trick to follow it.
- Proper indentation always (4 spaces in C#, 2 in TypeScript/JSON/YAML, enforced by `.editorconfig` and the formatter). Blank lines separate logical steps inside a method; one blank line between members; no blank-line walls, no cramming.
- Whitespace is part of the code: spaces around operators and after commas, one statement per line, no multi-statement one-liners, no nested ternaries, no expression bodies longer than one short line.
- Comments where they earn their place: explain why, not what; a non-obvious decision, a workaround, a domain rule, a link to the work item or decision. No comments that restate the code, no commented-out code, no TODOs without a work item ID.
- Names say what things are: full words, no abbreviations except universally known ones (`id`, `url`, `db`); booleans read as questions (`isPublished`, `hasQuota`).
- Methods fit on a screen; a method that needs a scroll bar is split. Files hold one public type.
- Formatting is never discussed in review: `dotnet format` and Prettier run in CI and fail the build on drift.

## Security, performance, audit, tests — how the principles become code
- Security: authorisation by policy on every action (`[Authorize(Policy = ...)]`, never a bare `[Authorize]`); resource-based checks through handlers; tenant id from the token, never from the request; RLS set per connection; parameterised queries only; input validated at the boundary and invariants enforced in the domain; secrets via environment or secret store; CSP, HSTS, secure cookies, rate limits on by default; password hashing, two-factor and lockout only through ASP.NET Core Identity, never hand-rolled; dependency audit (`dotnet list package --vulnerable`, `pnpm audit`) in CI; threat notes in the PR for anything touching auth, uploads, exports or domains.
- Performance: budgets in `AGENTS.md` are tested, not hoped for — load tests for the runner submit path and the results queries, Lighthouse CI for the runner, EF query logging with a hard rule against N+1 (`AsNoTracking`, projections, `AsSplitQuery` where needed, indexes added with the migration that needs them), pagination everywhere, async I/O everywhere, no `.Result`/`.Wait()`.
- Auditable and observable: a single audit write path (`IAuditWriter`) used by handlers and jobs in the same transaction as the change; structured logs, metrics and traces through the .NET abstractions (`ILogger`, `Meter`, `ActivitySource`) and `packages/telemetry` in the apps. What is audited, what every entry carries, the logging rules and the metrics list are in `observability.md`; they are part of these standards.
- TDD: pure red → green → refactor on every layer, backend and frontend alike. The full rules, what is tested where, and the CI gates are in `testing.md`; they are part of these standards.
- Snappy: TanStack Query with optimistic updates and cache invalidation; skeleton states, never blocking spinners; route-level code splitting; prefetch on hover for the designer's outline; SignalR for live counts instead of polling; runner answers saved locally before the network call.

## .NET backend
- Target: current LTS .NET (10). C# with nullable enabled, `TreatWarningsAsErrors`, analyzers on, `.editorconfig` committed.
- Structure: modular monolith, organised by feature (vertical slices) inside the projects in `architecture.md`. A feature folder holds its controllers (App API and, where the feature is public, Public API), request/response DTOs, validator and handler side by side. Tests live in the `Askwerk.Tests.*` projects, in folders that mirror the feature folders (`architecture.md`, Test architecture).
- Endpoints: attribute-routed controllers, no minimal APIs. One controller per resource, living in its feature folder next to its DTOs, validator and handler. `[ApiController]`, typed `ActionResult<T>`, `[ProducesResponseType]` on every action. App API controllers route under `/api/app` and go into the App API document; Public API controllers route under `/api/v1`, have their own DTOs (never the App API's), and go into the public document.
- Base classes and shared pieces are welcome where they remove real repetition: `ApiControllerBase` (current tenant and user, standard ProblemDetails helpers, paging helpers), `TenantScopedController` for anything under a tenant, common DTOs (`PagedRequest`, `PagedResult<T>`, `ProblemDetails` extensions, `TranslatedText`, `AuditedDto`), shared validators and EF configurations. Inherit for shared behaviour, compose for optional behaviour; never inherit only to share a field. A base class that needs `virtual` hooks in three places is a sign the hierarchy is wrong.
- XML documentation comments (`/// <summary>`) on every public type and member, and on every controller action: they are the source of the OpenAPI descriptions (`GenerateDocumentationFile` on, warnings for missing docs are errors in `Askwerk.Api` and `Askwerk.Domain`). Write them for the API consumer: what it does, what it returns, which errors are possible. Private members get a summary only when it helps.
- No MediatR. Handlers are plain classes registered in DI; a tiny in-house dispatcher if one is ever needed.
- Validation: DataAnnotations and `IValidatableObject` on request DTOs at the boundary (run automatically by `[ApiController]`), plus small shared validators of our own in `Askwerk.Application` for rules annotations cannot express. Error messages come from the DE/EN catalogue. Domain invariants live in the domain model. No validation library.
- Persistence: EF Core + Npgsql, migrations in `Askwerk.Infrastructure`, JSONB via `jsonb` columns with `System.Text.Json`. Queries in repositories or feature-local query classes; no `DbContext` in endpoints. RLS: every request sets `app.tenant_id` on the connection.
- Mapping: hand-written, explicit (a `ToDto`/`ToEntity` method in the feature folder). No mapping library.
- Jobs: our own PostgreSQL job table and `BackgroundService` (see `architecture.md`). A job handler implements `IJobHandler<TPayload>`; payloads are versioned records serialised with `System.Text.Json`, never serialised method calls. Enqueue through the same `DbContext` transaction as the change that causes the job.
- Expression language: parser and evaluator in `Askwerk.Domain`; the TypeScript evaluator in `packages/schema` must pass the same golden test corpus (`/src/frontend/packages/schema/tests/corpus/*.json`, shared by both).
- Import/export converters: one class per format implementing `IImporter`/`IExporter`; golden files in `Askwerk.Tests.Application/golden/<format>/`; SPSS and Stata as CSV plus a generated syntax file (`.sps`, `.do`) that applies variable labels, value labels and types. No native libraries.
- Logging, metrics, tracing: `Microsoft.Extensions.Logging` (structured, `LoggerMessage` source generators on hot paths, JSON console formatter), `System.Diagnostics.Metrics`, `ActivitySource`; exported by OpenTelemetry (OTLP). No personal data, answer content or secrets in logs. Rules in `observability.md`.
- Tests: xUnit with its built-in `Assert`, Testcontainers for PostgreSQL (or an existing PostgreSQL via the `ASKWERK_TEST_DB` connection string, so Docker is not required), our own snapshot helper for OpenAPI documents and export golden files. Coverage is not a target; untested logic is.
- Style: records for DTOs, `sealed` by default, async all the way, cancellation tokens on every I/O call, no static mutable state, no reflection-driven magic.

## TypeScript frontend
- React 19 + TypeScript strict, Vite, pnpm workspaces. ESLint (typescript-eslint, react-hooks, jsx-a11y) and Prettier committed.
- Routing and data: TanStack Router and TanStack Query (MIT). Server state only in Query; local UI state in components and React context. No global state library.
- Forms: React 19 form actions (`useActionState`, `useFormStatus`) with zod schemas and a small form helper of our own in `packages/ui` (field errors, dirty state, autosave). No form library. The runner renders survey questions from the schema, not from a form helper.
- Styling: Tailwind CSS v4 (MIT, build time only) with the design tokens from `packages/ui` as CSS variables; no inline style objects except computed positions. No component kit that brings its own look.
- Primitives: Radix UI (MIT) for accessible behaviour (dialog, popover, menu, select, tabs); every primitive is wrapped once in `packages/ui` and only the wrapper is used in apps.
- Drag and drop: dnd-kit (MIT), wrapped once in `packages/ui`.
- Motion: CSS transitions and the View Transitions API, respecting `prefers-reduced-motion`. No animation library.
- Charts: our own SVG chart components in `packages/ui` (bar, stacked bar, distribution, line, gauge, heatmap), each with an accessible table view. No chart library.
- Icons: Lucide (ISC) SVGs we actually use, copied into `packages/ui/icons` with the licence notice. No icon package.
- i18n: our own layer in `packages/schema`: typed German and English catalogues (`de.ts`, `en.ts`) and the native `Intl` APIs (`PluralRules`, `NumberFormat`, `DateTimeFormat`, `RelativeTimeFormat`). The type-check fails when a key is missing in either language. Every string goes through it; no hard-coded text. No i18n framework.
- Frontend telemetry: only through `packages/telemetry` (`observability.md`); no `console.*` in production code, no third-party analytics or error-tracking scripts.
- Runner constraints: no admin imports, bundle budget 120 kB gzipped for the first page, no third-party fonts loaded from the network by default, CSP-compatible (no inline scripts).
- Data access: the apps call the App API only through `packages/app-client` (generated from the App API's OpenAPI document), wrapped in TanStack Query hooks; tests swap in an in-memory fake transport. The apps never use the public SDKs.
- Tests: written first, per `testing.md`. Vitest + Testing Library for components, hooks and the evaluator; Playwright for flows; a `/dev/states` page in the designer's development build shows every `packages/ui` component in every state, both themes and both languages, and Playwright runs axe on it and on every app. No Storybook.

## API conventions
- Both surfaces (`/api/app/...` and `/api/v1/...`): plural nouns, kebab-case paths, camelCase JSON, RFC 7807 problem details for errors, cursor pagination, `ETag`/`If-Match` on definitions, idempotency keys on response submission.
- Public API: breaking changes only in a new version prefix; every public endpoint has an example in OpenAPI. App API: no versioning; changed together with its callers in the same PR.
- The full rules for both surfaces and the Public API (what is public, contract first, authentication, scopes, versioning, webhooks, embed, SDKs, MCP) are in `public-api.md`.

## Git hygiene
- See `workflow.md`. Small PRs, signed off (DCO), squash merge (the PR branch keeps its red → green → refactor history until then), no force-push to `main`, generated files (migrations, OpenAPI snapshot, i18n catalogues) committed.
