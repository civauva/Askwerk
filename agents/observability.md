# observability.md — audit, logs, metrics and traces, from day one

Askwerk is observable and auditable from its first runnable commit, on the backend and in every frontend app. These are not features added later: the runnable skeleton (AWK-11) delivers the audit write path, the OpenTelemetry wiring and the frontend telemetry module before any product feature is built on top, and every later feature adds its own audit entries, logs and metrics in the same PR.

## Two different things
- **Audit log**: a business record of who did what to which entity. Stored in PostgreSQL, written in the same transaction as the change, append-only, shown and exported in the GUI, kept by retention policy. It is product data, not telemetry, and it never depends on an external system.
- **Telemetry** (logs, metrics, traces): operational signals for running and debugging Askwerk. Emitted through OpenTelemetry. Askwerk works fully without a telemetry backend; with one configured, everything flows there over OTLP.

A log line never replaces an audit entry, and audit entries are never sent as logs.

## Audit log
- One write path: `IAuditWriter` in `Askwerk.Application`, called by handlers and job handlers, writing an `AuditLog` row through the same `DbContext` transaction as the change. If the change commits, its audit entry exists; if it rolls back, neither does.
- Every entry: timestamp (UTC), tenant, actor kind (user, API key, OAuth client, job, system) and actor id, action (stable dotted name, e.g. `survey.published`), entity type and id, before/after diff for definitions and settings, trace id, IP address and user agent where the anonymity mode allows.
- What is audited, at least: sign-in success and failure, two-factor and password changes, lockouts; user, role and membership changes; API key and OAuth client creation, use summary and revocation; every settings change (instance, tenant, workspace, survey, domains, email, external sign-in, webhooks); survey definition changes, publish, close, archive, delete; participant import and deletion; response edits, deletions and GDPR erasure; every results export and download (who took the data out is as important as who changed it); purge and retention jobs.
- Respondents are not actors in the audit log. Submitting a response is recorded as data, not as an audit entry, so anonymity modes are never undermined.
- Tamper evidence: each entry stores a hash of its content plus the previous entry's hash per tenant (a hash chain). A verification job and a "Verify integrity" button in the GUI report a broken chain.
- Append-only enforced in the database: the application role has `INSERT` and `SELECT` on `AuditLog` only; retention purge runs as a separate role and is itself audited.
- GUI: an audit screen per tenant (filter by actor, entity, action, time; open the diff), an audit tab on surveys and settings pages, export as CSV and JSON. Also reachable through the Public API (`audit:read` scope) when that is decided public.

## Telemetry on the backend
- **Standard**: OpenTelemetry .NET (Apache-2.0) for traces, metrics and logs. Logs go through `Microsoft.Extensions.Logging` and are exported by the OpenTelemetry logging provider; metrics use `System.Diagnostics.Metrics` (`Meter`); traces use `ActivitySource`. Code depends only on these .NET abstractions, never on an exporter.
- **Instrumentation**: ASP.NET Core and `HttpClient` (built-in meters and activities), Npgsql (its own OpenTelemetry support), EF Core, SignalR, the job runner and the webhook sender (own `ActivitySource` and `Meter` named `Askwerk.*`).
- **Without a backend configured** (the default install): JSON structured logs on the console (`docker compose logs` works), `/health` and `/health/ready`, and the in-app Diagnostics screen (below). No collector container is added to the install.
- **With a backend**: the operator sets an OTLP endpoint (any OpenTelemetry-compatible system: Grafana stack, SigNoz, Jaeger, Azure Monitor, Datadog, …) in Instance settings → Observability, or with the standard `OTEL_*` environment variables, which take precedence. Changing the endpoint in the GUI takes effect after the Askwerk process restarts; the screen says so and offers the restart. This is the one setting that needs a restart.
- **Correlation**: W3C trace context everywhere. Every log record carries trace and span id; every problem-details response carries `traceId`; the frontend shows it in error messages ("Reference: …") so a user can report it. Jobs and webhooks continue the trace of the request that enqueued them (the trace context is stored with the job row).
- **Metrics, at least**: HTTP request rate, duration and errors per route (built-in); database pool and command duration; job queue depth, wait time, duration, failures and retries per job kind; webhook deliveries by outcome and latency; emails sent and failed; responses started and completed per minute; import and export duration and size; sign-in failures and lockouts; frontend signals forwarded from the apps (below).
- **Logging rules**: structured messages with named placeholders, never string interpolation; `LoggerMessage` source-generated methods for anything on a hot path; stable event ids per feature; levels used consistently (`Error` = someone must act, `Warning` = degraded but handled, `Information` = business-relevant step, `Debug` off in production). No personal data, no answer content, no secrets, no tokens in logs or span attributes; ids instead of names and emails.

## Telemetry in the frontend (designer and runner)
- **Own module, no SDK**: `packages/telemetry` (AGPL-3.0), a small TypeScript module of our own. The OpenTelemetry browser SDK is not used: it would cost a large share of the runner's 120 kB budget, and the parts we need are small. The module is shared by designer and runner and stays under 3 kB gzipped in the runner.
- **What it collects**:
  - Logs: uncaught errors, unhandled promise rejections, React error boundaries, failed API calls (status, route template, duration, trace id), and explicit `log.warn` / `log.error` calls. No `console.*` in production code.
  - Metrics: Core Web Vitals (LCP, INP, CLS, TTFB) per app and route, measured with the browser's own `PerformanceObserver`; API call duration and failure rate as seen by the client; runner-specific: time to first question, page-to-page time, submit duration, answers saved offline and later synced.
  - Traces: every API call sends a `traceparent` header generated by the module, so a click in the designer and the server work it caused share one trace id.
- **Transport**: batched, sent with `navigator.sendBeacon` (or `fetch` with `keepalive`) to `POST /api/app/telemetry` on the App API, which validates, rate-limits and feeds the records into the backend's OpenTelemetry pipeline (logs and metrics with `service.name` = `askwerk-designer` or `askwerk-runner`). The browser never talks to a telemetry backend directly, so no extra CORS, keys or endpoints are exposed.
- **Privacy**: no cookies, no user or respondent identifiers, no answer content, no URL query strings, no free text from forms. The runner sends only technical signals and respects the survey's anonymity mode; a tenant can switch runner telemetry off. No product analytics (click tracking, session replay, heatmaps) — that is a different thing and not part of this.

## Diagnostics screen (no telemetry backend needed)
Instance settings → Diagnostics shows, from in-process counters and the database: health of each component, version, uptime, request and error rates over the last hour, job queue depth and failures, webhook failures, recent errors (the last 200 error-level log records kept in memory, with trace ids), and the frontend error rate. It is a first look for operators without an observability stack, not a replacement for one.

## Tests
- Audit: every handler test that changes data asserts the audit entry (action, actor, entity, diff). Integration tests prove the entry is written in the same transaction, the application role cannot update or delete audit rows, and the hash chain verifies.
- Telemetry: integration tests use an in-memory exporter (OpenTelemetry's in-memory exporter, development only) to assert that a request produces a trace with the expected spans, that logs carry the trace id, and that no log or attribute contains personal data from the test fixtures. `packages/telemetry` is unit-tested with Vitest; a Playwright test checks that an error in the runner reaches `/api/app/telemetry` with a trace id.

## Definition of done (observability part)
A feature is not done until: its data and settings changes write audit entries; its new code paths log at the right level with structured fields; anything with a duration, a queue or a failure mode has a metric; its frontend calls go through `packages/app-client`, which carries the trace context; and none of it leaks personal data.
