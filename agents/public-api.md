# public-api.md — the Public API: a stable subset for integrators

This file holds the rules; how the API is built (pipeline, authentication paths, outbox, SDK pipeline, MCP) is in `architecture.md`, section Public API.

## Two API surfaces, one application
- **App API** (`/api/app`): what the designer and runner call. Cookie session for signed-in users, respondent session for the runner. Unversioned, not published, not promised to anyone; it changes together with the GUI in the same PR. The apps reach it through a client generated from its own OpenAPI document (`packages/app-client`).
- **Public API** (`/api/v1`): a deliberately chosen subset of what the product can do, for integrators. API keys or OAuth client credentials. Versioned, documented, snapshot-tested and stable under the deprecation policy below.
- Both surfaces are thin controllers over the **same application handlers**: business logic, validation, authorisation policies and audit entries exist once. The surfaces differ only in routing, authentication, and request/response DTOs.
- The Public API uses the same vocabulary as the import and export files, so automation never needs a second set of names.

## What is public
- Initial scope: surveys (list, read, create from Askwerk JSON, publish, close), participants (list, create, import), responses (list, read, submit), results export (asynchronous job), webhooks (manage), and the jobs needed to poll those exports and imports.
- Not public unless a work item decides otherwise: designer editing operations (pages, questions, logic one by one), themes, analytics views, user and role management, instance and tenant settings, domains, audit screens.
- Making an endpoint public is a decision in its work item, recorded in the contract sketch. A feature that is not in the Public API is not a gap; it is the default.
- Public so far: list and read surveys, start an export, poll it and download its file (AWK-25), webhooks and the OAuth token endpoint (AWK-32), with API keys or OAuth access tokens, and the MCP server at /mcp with four tools (AWK-32). SDKs in AWK-26.

## Contract first
- The OpenAPI 3.1 document is the contract. It is generated from the controllers and their XML documentation at build time, committed at `docs/api/openapi.json`, and snapshot-tested (`testing.md`). A change to the snapshot is an API change and is reviewed as one.
- A new public endpoint starts as a contract sketch (path, method, request, response, errors, example) posted to its work item. That sketch is the "wireframe" for API work and is approved before the endpoint is built (`workflow.md`, approval gate).
- Every public endpoint has a summary, a description, a request and response example, and every possible error documented.
- A PR that adds or changes a Public API endpoint ships in the same PR: the endpoint(s), tests (including authorisation and tenant isolation), the updated OpenAPI snapshot, regenerated SDKs, `docs/api/` and a `CHANGELOG.md` entry. A PR that touches only the App API does none of the public steps.

## Shape
- Base path `/api/v1`. Plural nouns, kebab-case paths, camelCase JSON, ISO 8601 UTC timestamps, ids as opaque strings.
- Errors: RFC 7807 problem details with a stable `type` URI per error and translated `title`/`detail` (DE/EN by `Accept-Language`).
- Pagination: cursor-based (`cursor`, `limit`, `nextCursor`), never offsets on large collections.
- Concurrency: `ETag` / `If-Match` on survey definitions and settings; `412` on a stale write.
- Idempotency: `Idempotency-Key` header on response submission and on every create that a client may retry.
- Filtering and sorting: documented query parameters per collection; no generic query language.
- Bulk: results export and response import are asynchronous jobs (`202` with a job resource to poll or a webhook on completion).

## Authentication and authorisation
- **API keys** per tenant, created in Settings → API: shown once, stored hashed, scoped, optionally restricted to a workspace, with expiry and last-used time. Sent as `Authorization: Bearer <key>`.
- **OAuth 2.0 client credentials** for server-to-server integrations: Askwerk issues short-lived opaque access tokens (15 minutes, stored as their hash, revocable at once with the client) from its own token endpoint (`/api/v1/oauth/token`). No identity broker and no JWT library are involved (AWK-32).
- Cookie sessions are for the App API only; the Public API does not accept them.
- Third-party "sign in with Askwerk" apps (OAuth authorization-code flow) are not in v1; adding them is its own work item and decision.
- Scopes (initial set): `surveys:read`, `surveys:write`, `responses:read`, `responses:write`, `participants:read`, `participants:write`, `webhooks:manage`, `settings:read`, `settings:write`. Every endpoint names its policy and scope; every call is audited with the key or client as actor.
- Rate limits per tenant and per key, with `RateLimit-*` headers and `429` + `Retry-After`.

## Versioning and deprecation
- Additive changes (new endpoints, new optional fields, new enum values documented as open) stay in `v1`. Clients must ignore unknown fields.
- Breaking changes only in a new prefix (`/api/v2`). The previous version stays available for at least 12 months after the new one is released, announced with `Deprecation` and `Sunset` headers and in `CHANGELOG.md`.
- Webhook payloads and file formats follow the same rule.

## Webhooks
- Configured in Settings → Webhooks: URL, events, secret; delivery log and replay in the GUI.
- Event catalogue (initial): `survey.published`, `survey.closed`, `response.started`, `response.completed`, `response.updated`, `participant.created`, `export.completed`, `import.completed`. Each event is documented with its payload schema in `docs/api/webhooks.md`.
- Delivery: HTTP POST, JSON body with `id`, `type`, `occurredAt`, `tenantId`, `data`; header `Askwerk-Signature` = HMAC-SHA256 over timestamp and body; retries with exponential backoff; at-least-once, so receivers deduplicate by `id`. Delivered through the job table (transactional outbox, `architecture.md`).

## Embed
- Script and iframe snippets from the Publish screen; URL prefill of answers and hidden fields.
- postMessage events to the host page: `askwerk:started`, `askwerk:page`, `askwerk:completed`, `askwerk:resize`, each with a documented payload. The embed script lives in `packages/embed` (Apache-2.0).

## SDKs
- TypeScript, C# and Python, generated from the OpenAPI document in CI, with zero runtime dependencies beyond each platform's standard library. Hand-written code only for the auth helper (API key and client-credentials token refresh).
- Published to npm, NuGet and PyPI under the `askwerk` name, versioned with the API. Source in `src/sdks/` (Apache-2.0). Tested against a real API (`testing.md`).

## MCP server
- `/mcp` on the API, exposing Public API operations as tools (create survey, publish, list responses, export); it never reaches further than the Public API does. Authenticated with an API key; scopes and audit as for any API call. Part of the API, therefore AGPL-3.0.

## Documentation and examples
- `docs/api/`: the OpenAPI document, an endpoint reference rendered from it, authentication guide, webhooks catalogue, embed events, error catalogue, versioning policy.
- `docs/integrations/`: a plain curl walkthrough first, then examples (n8n, Zapier, HubSpot, Dynamics). Examples are Apache-2.0 and are not dependencies.
- A stranger must be able to go from zero to "survey created, response received by webhook" using only `docs/api/`.
