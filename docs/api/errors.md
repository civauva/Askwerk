<!-- SPDX-License-Identifier: Apache-2.0 -->
# Errors

Every error of the Public API is an [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) problem
(`Content-Type: application/problem+json`). `type` is stable and names the problem; `title` and `detail` are in
German when `Accept-Language` starts with `de`, in English otherwise; `traceId` helps an administrator find the call
in the logs.

```json
{
  "type": "https://askwerk.org/problems/not-published",
  "title": "The survey is not published.",
  "status": 409,
  "detail": "Publish it before exporting its responses.",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

The type URIs are names; they need not open in a browser.

| type (after `https://askwerk.org/problems/`) | Status | Meaning |
|---|---|---|
| `unauthorized` | 401 | No key, or an unknown, revoked or expired key. |
| `scope-missing` | 403 | The key lacks the scope the endpoint names. |
| `rate-limited` | 429 | Too many calls; wait as long as `Retry-After` says. |
| `survey-not-found` | 404 | No such survey in the key's organisation. |
| `unknown-format` | 400 | Not one of `excel`, `csv`, `spss`, `stata`, `json`. |
| `not-published` | 409 | The survey has no published version to export. |
| `too-many-exports` | 429 | Three exports of the organisation are already being prepared. |
| `export-not-found` | 404 | No such export in the key's organisation. |
| `export-not-ready` | 409 | The file is not there yet; poll the export. |
| `export-expired` | 410 | The file was deleted after 7 days; start a new export. |
| `contact-invalid` | 400 | The contact is not valid; `errors` names each field with its codes (`email-required`, `email-invalid`, `language-invalid`, `too-long`, `too-many-groups`). |
| `contact-not-found` | 404 | No such contact in the key's organisation. |
| `contact-email-taken` | 409 | The External ID found one contact and the email another. |
| `export-failed` | — | In an export's `error`: it could not be prepared. Nothing was lost; start a new one. |
| `too-many-rows-for-excel` | — | In an export's `error`: more rows than one Excel sheet holds; export as CSV. |
