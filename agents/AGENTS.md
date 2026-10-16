# AGENTS.md — how to work on Askwerk

Askwerk is a self-hostable survey platform: deep enough for complex questionnaires, simple enough for anyone, open to integration through its API. We do not compare Askwerk with other products in documentation, the UI or marketing text; other products are named only where Askwerk imports or exports their formats. Owner: Dražan (e-manuel software artisans). This folder is the single source of instructions for every coding agent and every human contributor. Nothing instruction-like lives deeper in the tree.

## Reading order
1. `workflow.md` — the approval gate, Throughly work items, branches, definition of done. Read before touching anything.
2. `testing.md` — pure TDD on every layer: the cycle, how tests are written, what is tested where, CI gates.
3. `architecture.md` — monorepo layout, services, hosting, publishing to customer domains, how the App API and the Public API are built, test architecture and CI.
4. `observability.md` — audit log and telemetry (logs, metrics, traces) on backend and frontend, built from day one.
5. `public-api.md` — the Public API (a deliberately chosen, stable subset for integrators) and how it differs from the App API: contract, authentication, versioning, webhooks, embed, SDKs, MCP.
6. `data-model.md` — entities, versioning, tenancy, i18n, codebook.
7. `coding-standards.md` — .NET and TypeScript rules, dependencies, licensing.
8. `ui-design-system.md` — the visual language and the component rules for all three apps.
9. `extensibility.md` — what integration means here (files, API, webhooks, embed) and what it is not (no plugin system).
10. `decisions.md` — the decision log. Append, never rewrite history.

## Engineering principles (in this order when they conflict)
1. **Security first.** Every endpoint authorised by policy, every query tenant-scoped by RLS, every input validated, every secret outside the repo, every dependency reviewed. A feature that is not secure is not done.
2. **Performance first.** Budgets are requirements: runner first paint < 1 s on a mid-range phone, API p95 < 200 ms for reads and < 500 ms for writes at 1 M responses per survey, designer interactions < 100 ms. Measure before and after; no feature may regress a budget.
3. **Auditable.** Who did what, when, to which entity, from where — append-only, queryable in the GUI, exportable. Applies to users, API keys and background jobs alike. If it changes data or settings, or takes data out (exports), it is in the audit log. Alongside it, every part of the system — backend and frontend — emits structured logs, metrics and traces through OpenTelemetry. Both exist from the first runnable commit, not added later. Rules in `observability.md`.
4. **Pure TDD.** On every layer — backend, frontend, SDKs, deployment — no production code without a failing test that requires it: red → green → refactor, visible in the PR's commits. No PR without the tests that drove it. Untested code is a defect. Rules in `testing.md`.
5. **Snappy.** The product must feel instant: optimistic updates, skeletons instead of spinners, no full-page reloads, no action that takes longer than it needs, no waiting on the network for things the browser already knows.

## Non-negotiables
- Askwerk is open source: AGPL-3.0 for the product, Apache-2.0 for everything an integrator links into their own system (schema package, embed script, SDKs). Everything in this repo is written to be read, built and run by strangers: no hidden steps, no private dependencies, no secrets in code, `docker compose up` must work on a clean machine.
- Askwerk works on its own; integrations are optional. The product has two API surfaces in the same process: the **App API** (`/api/app`), which the designer and runner use, unversioned and free to change with the GUI; and the **Public API** (`/api/v1`), a deliberately chosen subset of features for integrators, versioned, documented and stable. An endpoint becomes public only when its work item decides so; then it ships its contract, tests, regenerated SDKs, webhook events where relevant, and docs in the same PR. Both surfaces call the same application handlers, so business logic, authorisation and audit exist once. Rules in `public-api.md`.
- Nothing is built before its visuals (wireframes, mockups) were approved by Dražan in Claude Code, or, for an outside contribution, by a maintainer in the pull request. See `workflow.md`.
- No commercial or paid third-party component, library, service or licence anywhere. Open-source only, and only when justified in `decisions.md`.
- No hard dependencies. The default install is the Askwerk image plus PostgreSQL; Caddy is an optional front proxy. No extra services or containers, no open-core or paid-tier libraries. Native code alone is no reason to refuse a library; an unneeded dependency or a non-free licence is (owner, 2026-10-14). Prefer what .NET and the browser already provide, or a small implementation of our own. Every dependency is justified in `decisions.md` with its licence, why we cannot write it ourselves in reasonable time, and how we would remove it. Rules in `coding-standards.md`.
- Every work item lives in Throughly (my.throughly.app, project Askwerk, key `AWK`, MCP at my.throughly.app/mcp). Rendered visuals are posted in that work item's comments; other artifacts are linked there. Throughly is the maintainers' tracker; outside contributors work through GitHub issues and pull requests. See `workflow.md`.
- Configuration is done in the GUI. A customer never edits a config file. Publishing a survey to a customer's own domain is one screen.
- Two product languages from day one: German and English. Every user-facing string is translatable; every survey text is a translation map.
- The respondent app (runner) is a separate, small, accessible bundle. Never import admin code into it.
- A survey version is immutable once published. Responses bind to a version. Never migrate collected data to fit a definition change.

## Folder map
```
/CLAUDE.md          pointer to this folder
/agents/            all agent instructions (this folder)
/src/               monorepo source (see architecture.md)
/docs/              human-facing product, user, API and contributor documentation, including canonical diagrams
/design/            wireframe and mockup sources, one folder per work item (`design/AWK-N-slug/`)
/deploy/            Docker Compose, Helm, reverse-proxy config
/bin/               build, CI and utility scripts (any CI system runs these)
```

## When unsure
Stop and ask in Claude Code. Do not pick a library, change the data model, or invent a screen without a work item and an approved visual.
