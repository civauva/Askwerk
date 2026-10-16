# testing.md — pure TDD, every layer

Askwerk is built test-first, everywhere: backend, frontend, SDKs, deployment. There is no layer where "we test it later".

## The cycle (no exceptions)
1. **Red.** Write one small test for the next behaviour. Run it and watch it fail for the expected reason (not a compile error in the test, not a typo). If it passes straight away, the test is wrong or the behaviour already exists.
2. **Green.** Write the least production code that makes it pass. Nothing speculative.
3. **Refactor.** Clean up production and test code with all tests green. No new behaviour in this step.
4. Repeat.

Rules:
- No production code without a failing test that requires it. This includes bug fixes (first a test that reproduces the bug), configuration surfaces, migrations, Dockerfiles (through the container smoke test, below) and UI components.
- One behaviour per cycle. Several failing tests at once means the step was too big.
- The cycle is visible in the PR's commits: `test: …` (red), then `feat: …` / `fix: …` (green), then `refactor: …` when there is something to clean. Small cycles may combine test and code in one commit only when the PR description says which test was seen failing.
- A test is never changed to make it pass. If the expectation was wrong, say so in the commit message.
- Untested code found in review is a defect: the PR goes back.

## How much to run, and when (owner rule, 2026-10-14)
Tests are fast only if we run the right ones. Three levels, never more than needed:
1. **Inside a red/green cycle:** only the test file or class being worked on: `pnpm exec vitest run src/x.test.tsx` in the package, or `dotnet run -- -class Askwerk.Tests.Api.XTests` in the test project. Seconds, not minutes.
2. **At the end of a step** (before pushing it): `bin/test-changed.sh` runs only the test projects and packages the changes can reach, and in the frontend only the test files that import a changed file. `--since HEAD` limits it to what is not committed yet. No Docker image, no browser flows.
3. **Once per merge:** `bin/test-all.sh --full` (the image, the browser flows, the smoke test), the same as CI. Not after every step, and never several times in a row without a change in between.
Testing never holds up the work (owner, 2026-10-14):
- Runs of level 2 and 3 go in the background while the next piece of work starts; nobody waits for them. A failure is fixed when it is reported, in the next commit.
- The full run (level 3) is batched: once for several steps together, when the owner asks for a merge, not after every step.
- A busy machine is the owner's machine: no two long runs at the same time.

## How tests are written
- One behaviour per test, named after it: `Method_Scenario_Expectation` in C#, `it("does X when Y")` in TypeScript.
- Arrange / act / assert, separated by blank lines. No loops, conditionals or calculations in tests; expected values are literals.
- Test data comes from builders of our own (`SurveyBuilder`, `ResponseBuilder`, `TenantBuilder` in C#; matching factories in TypeScript), so a test states only what matters to it.
- Do not mock what we own when the real thing is cheap: use the real domain objects, the real expression evaluator, a real PostgreSQL. Fakes (hand-written, no mocking library) only at the edges: clock, email, file storage, outbound HTTP.
- Tests are deterministic: injected clock (`TimeProvider`), fixed random seeds, no sleeps, no dependency on test order or on the network.
- Every test runs locally with one command per layer (`bin/test-backend.sh`, `bin/test-frontend.sh`, `bin/test-e2e.sh`, `bin/test-deploy.sh`, `bin/test-all.sh`) and the same scripts run in CI. Where each kind of test lives and the CI stages are in `architecture.md`, Test architecture.

## Per layer

### Backend (.NET)
| Layer | What is tested | How |
|---|---|---|
| Domain | entities, value objects, invariants, survey versioning rules, codebook derivation | xUnit, plain objects, no database |
| Expression language | parser, evaluator, error messages | shared golden corpus `src/frontend/packages/schema/tests/corpus/*.json`, run by both the C# and the TypeScript evaluator; a case is added to the corpus before the code that handles it |
| Application | use cases, authorisation policies, validation, audit entries (every data change asserts its entry) | xUnit, real domain, fakes only at the edges |
| Persistence | mappings, migrations, queries, RLS tenant isolation, N+1 guard | Testcontainers PostgreSQL (or `ASKWERK_TEST_DB`); every migration is tested up from an empty database and against the previous schema with data |
| API | every endpoint of both surfaces (`/api/app` and `/api/v1`): status codes, problem details, pagination, ETags, idempotency, rate limits; App API rejects API keys, Public API rejects cookie sessions | `WebApplicationFactory` against a real PostgreSQL; for each endpoint one test per role proving the authorisation policy and one proving another tenant's data is invisible |
| API contract | the Public API's OpenAPI document | snapshot of `docs/api/openapi.json` (own helper; `ASKWERK_UPDATE_SNAPSHOTS=1` rewrites it for review); any change to the snapshot is reviewed as a public API change (see `public-api.md`). The App API document is not snapshot-reviewed. |
| Jobs | enqueue in the same transaction, retry and backoff, single execution, scheduled run | integration tests against PostgreSQL with an injected clock |
| Webhooks | HMAC signature, retries, delivery log, replay | integration tests with a local fake HTTP receiver |
| Import/export | every format, both directions | golden files in `Askwerk.Tests.Application/golden/<format>/`; a new format starts with its golden file |
| Audit and telemetry | audit written in the same transaction, application role cannot update or delete audit rows, hash chain verifies; requests produce the expected spans, logs carry trace ids, no personal data in logs or attributes | integration tests against PostgreSQL; OpenTelemetry in-memory exporter (development only); see `observability.md` |
| Authentication | sign-in, two-factor, lockout, reset, external OIDC/LDAP mapping | integration tests; OIDC against a small fake provider in the test project |

### Frontend (TypeScript)
| Layer | What is tested | How |
|---|---|---|
| `packages/schema` | schema types, expression parser and evaluator, i18n layer (plurals, formats, missing keys) | Vitest, golden corpus shared with C# |
| `packages/ui` | every component in every state (default, hover, focus-visible, active, disabled, loading, error), both themes, both languages, keyboard behaviour | Vitest + Testing Library (queries by role and label, as a user would find them), axe on each state; the same states are shown on the `/dev/states` page |
| `apps/designer` | components, hooks, form helper, optimistic updates and rollback, route guards | Vitest + Testing Library; the App API is reached through `packages/app-client` with an in-memory fake transport of our own, never through a mocking library |
| `apps/runner` | rendering of every question type, validation, logic (show/hide/skip/jump/pipe), progress, answers saved locally before the network call, both layouts | Vitest + Testing Library for components; Playwright for whole surveys |
| `packages/telemetry` | error capture, Web Vitals, batching, `traceparent` on every API call, nothing personal in the payload | Vitest; Playwright checks a runner error reaches `/api/app/telemetry` with a trace id |
| User flows | create, edit, publish, respond, view results, configure a domain, import/export | Playwright against the real stack (API + PostgreSQL), phone and desktop viewports, DE and EN |
| Accessibility | WCAG 2.2 AA | axe in component tests and in every Playwright flow; zero violations |
| Performance | runner first paint < 1 s, 120 kB first-page budget, Lighthouse ≥ 95 | Lighthouse CI and a bundle-size check on every PR that touches the runner or `packages/ui` |
| Visual | approved wireframe / mockup | reviewed by a human against the approved visual; screenshots of the result posted to the work item |

### SDKs, embed and MCP
- Generated SDKs (TypeScript, C#, Python): a small test suite per language runs against a real API in CI (create survey, publish, submit response, read results, receive webhook).
- Embed script: Playwright on a host test page checks the iframe and script variants and every postMessage event.
- MCP server: integration tests call each tool against a real API.

### Deployment
- Container smoke test on every PR that touches a Dockerfile or `deploy/`: `docker compose build`, `docker compose up -d`, wait for `/health`, setup wizard reachable. This is the failing test that drives a Dockerfile change.
- Full smoke test on every release candidate: on a clean machine image, `docker compose build`, `docker compose up -d`, wait for `/health`, open the setup wizard, create the first admin, publish and answer a sample survey. The same run checks that the install works without Caddy and without SMTP.
- Upgrade test: start the previous release with data, upgrade, verify migrations and data.
- Backup and restore test: `bin/backup.sh`, wipe, `bin/restore.sh`, verify.

## CI gates (all required, on every PR)
On every PR: build, lint, type-check, all unit and integration tests, Public API contract snapshot, Playwright flows, axe, SPDX header check, dependency audit. On PRs that touch the runner or `packages/ui`: Lighthouse and the bundle budget. On PRs that touch a Dockerfile or `deploy/`: the container smoke test. A red gate blocks the merge; there is no "skip".

## Tools
xUnit and Testcontainers (.NET), plus a small snapshot helper of our own; Vitest, Testing Library, Playwright, axe-core and Lighthouse CI (TypeScript). All are development-only and open source (MIT or Apache-2.0); none ships in the product. No mocking libraries (Moq, NSubstitute, MSW, …): fakes are hand-written so tests stay readable and do not depend on a library's behaviour. No assertion libraries beyond xUnit's `Assert` and Vitest's `expect`.
