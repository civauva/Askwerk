# extensibility.md — how Askwerk integrates with other systems

Askwerk does the work; other systems only feed it and read it. Integration means getting questions and answers in and out without hassle, and letting another system trigger or be told about what happened. Nothing more.

## What integration is
1. Files in and out (primary). One Import button and one Export button, on the survey and on the results. Drop a LimeSurvey, Qualtrics, CSV or Excel file and get a draft; pick a format and get the file. Formats: see "Import and export formats" below.
2. Public API. REST + OpenAPI 3.1, generated SDKs, MCP server. It exposes a deliberately chosen subset of what the GUI can do (`public-api.md`, What is public), in the same JSON and CSV formats as the files, so automation never needs a second vocabulary.
3. Webhooks. Settings → Webhooks: paste a URL, tick events, see deliveries, retry. Signed (HMAC), retried with backoff, logged. Fire and forget: a webhook never blocks a respondent or waits for a reply.
4. Embed. Publish screen → copy a snippet (script or iframe), URL prefill, postMessage events for the host page (`public-api.md`, Embed).
5. Automation platforms (n8n, Zapier, Make) are built on 2 and 3 only.

## Import and export formats
- Import (survey definition → draft): Askwerk JSON (`askwerk.schema.json`), LimeSurvey (`.lss`, `.lsa`), Qualtrics (`.qsf`), CSV and Excel question lists. Anything that cannot be mapped is listed in the import report, never silently dropped.
- Export (results): CSV, Excel, SPSS and Stata (CSV plus a generated `.sps` / `.do` syntax file with variable labels, value labels and types), Askwerk JSON. Every export follows the codebook of the survey version (`data-model.md`, rule 3).
- Export (definition): Askwerk JSON, codebook (CSV and PDF).
- Detailed mapping rules per format are written in the work item that builds that converter and then documented in `docs/api/`.

## What integration is not
- No plugin system, no runtime-loaded code, no extension manifests, no synchronous hooks, no third-party code in the runner or the designer.
- External systems never change how Askwerk designs, runs or analyses a survey.
- Self-hosters who want another storage, email or export format add it in the open-source code through the existing interfaces (`IExporter`, `IImporter`, `IStorageProvider`, `IEmailProvider`) like any contributor. These interfaces exist for Askwerk's own providers; they are not a public plugin API and carry no compatibility promise.

## Rules for agents
- A feature request phrased as "let users plug in X" is answered with import/export, the API or webhooks. If none fits, stop and ask; do not add a new mechanism.
- Every format and every webhook event is documented once in `docs/api/` and does not change within a major version.
- Import and export must be reachable in at most two clicks from the survey and from the results.
