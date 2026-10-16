# data-model.md — entities and rules

Canonical diagram: `docs/architecture/data-model.svg` (approved as W3 in AWK-4; sources in `design/AWK-4-data-model/`). Keep this file, the diagram and the EF Core model in sync; a change here needs a migration and a `decisions.md` entry. Every data-model change is itself a work item with an approved entity diagram before migration.

## Tenancy and identity
- `Instance`: the installation (built in AWK-11).
- `InstanceSettings`: what the first-run setup recorded (AWK-16): instance name, base address, default language, time zone, the owner, and the optional SMTP settings (password encrypted). Setup is complete once it exists; afterwards the setup endpoints answer 404.
- `Tenant` (organisation): name, slug, brand kit, retention policy, settings JSONB (including allowed embed origins).
- `Workspace` (folder inside a tenant) groups surveys and permissions.
- `User` (table `app_user`, since `user` is reserved): one account per person across the installation (not per tenant): email, password hash (null for users who only sign in externally), security stamp, two-factor (authenticator key encrypted, recovery codes hashed), failed sign-ins and lockout end, passkeys (follow with the account screen), `source` (local | provisioned), `deactivated_at`.
- `Membership`: user, tenant, workspace (null = whole tenant), role (owner, admin, editor, analyst, viewer).
- `ExternalLoginProvider`: tenant, kind (handover | oidc | ldap | saml), issuer, audience, signing keys (rotating), clock skew, settings JSONB (secrets encrypted), claim-to-role mapping. Opt-in per tenant; off by default.
- `ExternalLogin`: user, provider, subject (the host system's user id, OIDC subject or LDAP DN), last sign-in.
- `UsedLaunchToken`: provider + `jti` of every accepted hand-over token until it expires, so each token works once.
- `Domain`: tenant, hostname, kind (system | custom | email), verification token, verified_at, `is_primary` (at most one per tenant; the default for links, QR codes and invitations). As built (AWK-7): table `domain` holds custom domains only (status pending | verified | failed, cname_found, txt_found, checked_at); the system address is the instance's base address, primary while no domain is. Email sender domains come later.
- `ApiKey` (tenant, optional workspace, prefix, hashed key, scopes, expiry, last used) and `OAuthClient` (client-credentials client: hashed secret, scopes), both issued by Askwerk (`public-api.md`). As built (AWK-25): `api_key` with name, prefix (first 12 characters), secret_hash (SHA-256), scopes, expires_at, revoked_at, created_by, last_used_at; no workspace yet; found by prefix through `api_key_lookup`.
- Every tenant table carries `tenant_id`; PostgreSQL row-level security enforces isolation. The policies read `askwerk.tenant_id` and `askwerk.user_id`, which the app sets on every connection from the authenticated session; `tenant` and `membership` are also visible to a signed-in user for their own memberships (decisions.md, AWK-16). Instance-level rows (Instance, the instance audit chain, system templates, jobs without a tenant) have none.

## Survey definition (versioned)
- `Survey`: tenant, workspace (empty until workspaces exist), title (internal name, not shown to respondents), status (draft | published | closed | archived), published_version, settings, theme_id (with AWK-17), languages[], default_language (of the current draft), anonymity mode. With AWK-29 (W24): closes_at (UTC, null for none; from then the survey counts as closed and a close-survey job sets the status) and one_response_per_person (default true: a browser whose response is complete may not start another).
- `SurveyVersion`: survey, number, definition JSONB (the full schema; source of truth for the definition), state (draft | published), published_at, created_by. Immutable once published (a database trigger rejects changes). Exactly one draft version may exist at a time.
- `Page`, `Question`, `Option`: relational index derived from the version's definition for querying and reporting.
- `Question`: stable `variable_name` (e.g. `Q3_1`) unique within a survey; `type`; texts as translation maps; validation; required; randomisation flags.
- `Option`: `code`, label (translation map), score, exclusive flag.
- `LogicRule`: kind (show | hide | skip | jump | pipe | quota | end), target, compiled expression (AST JSON), source text. As built (AWK-31): show, skip and end live in the definition itself (`showWhen` on questions and pages, `after` on pages), evaluated by `route` and `SurveyLogic` on a shared corpus; pipe and quota come later.
- `VariableMapping`: survey, from name, to name, since version (rule 2).
- `Theme`: tenant, name, is_default (one per tenant), tokens JSONB in the shared format of `packages/schema/src/theme.ts` (colours, fonts, scale, spacing, corners, buttons, answer controls, progress, layout preset, logo, header image, background), created/updated. A survey points to one with `theme_id`, or uses the default; without any theme the runner uses the Werkstatt preset. Custom CSS (flagged, web only) comes later (AWK-17).
- `Asset`: tenant, kind (image | font), content type found from the content, size, SHA-256, original name, storage key in file storage, created by/at. Rows are never changed; themes and surveys refer to them by id (AWK-17).
- `Template`: a survey definition saved for reuse, tenant or system scope.

## Collection
- `Collector`: survey, kind (link | embed | email | kiosk | qr), domain, open_at, close_at, quota, embed origins (narrowing the tenant's list), settings, tracking token.
- `Contact`: organisation-wide person record, kept in Askwerk or fed from a host system through the Public API: email, name, language, attributes JSONB, `source` (askwerk | host), deactivated_at.
- `Participant`: a contact invited to one survey (contact × survey): survey, contact (nullable for ad-hoc invitations), token, invitation state. As built (AWK-7): `participant` (email, name, language, token, sent_at, opened_at, bounced_at, reminders) per survey, unique by email; `invitation` (send, send_at, deadline, remind_at, host) for one sending; `unsubscribe` (tenant, email) so an organisation never invites that address again. Contacts across surveys come with the host sync (AWK-12/14). `response.participant_id` keeps a response with the person whose link started it. Participants can be removed (AWK-19), except those with a response.
- `Job` (AWK-7): tenant, kind, payload JSONB, run_at, attempts, locked_until, done_at, error. Workers claim due jobs with `claim_jobs` (FOR UPDATE SKIP LOCKED) and run them in the job's organisation; five tries with growing pauses.
- `Response`: survey_version (published only), collector, participant (nullable for anonymous), status (partial | complete | disqualified), started_at, completed_at, `updated_at`, `change_seq` (monotonic, for exact "changed since" pulls), metadata (only what the anonymity mode allows). As built (AWK-6): also survey, language, page_key (where to continue) and resume_token_hash (SHA-256 of the token in the respondent's HttpOnly cookie); collector and participant stay empty until distribution exists; change_seq comes from a sequence and a trigger on every update.
- `Answer` (as built in AWK-6: question_id → the published version's question index; number_value, text_value, date_value as written for the mode, option_codes[] (ranking in order), detail JSONB for the "Other" text, matrix rows and "Don't know"): **the source of truth for answers** — one row per response and question with typed columns (number, text, date, option codes[]) and a `detail` JSONB only for structured types (matrix, ranking, other-text, file reference); updated_at. The runner saves page by page into these rows. There is no JSON copy of the answers on `Response`.
- `ResponseEvent` (optional, append-only): page timings for analytics.

## Integration and operations
- `ExternalReference`: maps a host system's id to any Askwerk record — tenant, entity type, entity id, source (the host system), external id; unique per source in both directions. Askwerk's own ids stay the keys. Opt-in; empty without a host system. Ownership: the host system owns users, organisations and memberships; Askwerk owns survey content; deletes from the host become deactivations.
- `Job`: our own queue and schedule — kind, payload JSONB (a versioned record, never a serialised method call), run_at, status, attempts, last_error, locked_until, trace context. Written in the same transaction as the change that causes it.
- `Webhook`: tenant, url, secret, events[], active; `WebhookDelivery` per attempt (status, response code, duration; append-only), delivered through `Job`.
- `ImportJob`, `ExportJob`: kind, format, mapping JSONB, status, report JSONB, file reference (`IStorageProvider` key). As built for exports (AWK-25): `export` with survey, format, options JSONB, status (queued, running, ready, failed, expired), done/total, requested by a user or an API key, idempotency_key, job_id, file key/name/type/size, error, created/finished/expires_at (7 days).
- `ExportSchedule`: tenant, survey (null = all), format (long | wide | spss), schedule, last change sequence, destination JSONB — e.g. the nightly long-format export for a host's SQL Server.
- `IdempotencyKey`: tenant + key, request hash, stored response, expiry (`public-api.md`).
- `AuditLog`: append-only, written in the same transaction as the change; tenant (null = instance chain), sequence, actor kind and id, action, entity type and id, before/after diff, trace id, IP and user agent where the anonymity mode allows, hash and previous hash (per-chain). **Partitioned by month**: retention drops whole old partitions, so no database role ever needs DELETE on audit rows. Fields and rules in `observability.md`.
- Soft delete (`deleted_at`) everywhere user-visible; hard purge jobs for GDPR run under their own role and are audited.
- Responses and answers are not partitioned until load tests call for it; the design allows adding it without API changes.

## Rules
1. All human-readable text in definitions is a translation map `{ "de": "...", "en": "..." }`, never a plain string.
2. Variable names and option codes are stable across versions; renaming creates a `VariableMapping` entry, never a silent change.
3. A codebook (variables, codes, labels per version) is derivable from a version at any time and is the contract for every export.
4. Responses never reference a draft version.
5. Settings are JSONB with a versioned schema; unknown keys are rejected at save.
6. Host-system integration (external login kinds other than local, `ExternalReference`, export schedules, embedding) is opt-in per tenant and off by default.
