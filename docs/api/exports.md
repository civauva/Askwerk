<!-- SPDX-License-Identifier: Apache-2.0 -->
# Result exports

Results → Export in the designer, or `POST /api/v1/surveys/{surveyId}/exports` in the Public API, gives the responses
of a survey as one file. Every format follows the codebook of the version shown (the latest published one, or the one
chosen in the filter); responses to older versions are matched by question, so a renamed variable keeps its new name.
Responses come oldest first.

An export is prepared by a background job: the call answers `202` at once with the export, which reports its
progress (`queued`, `running`, then `ready`, `failed` or `expired`). A small export is ready within a second. The file
is kept 7 days, then deleted. At most three exports of an organisation are prepared at the same time. Every export
(`results.exported`) and every download (`export.downloaded`) is written to the audit log, with the person or the API
key as actor. Owners, admins, editors and analysts export in the designer; through the Public API a key needs the
scope `responses:read`.

## In the Public API

```sh
curl -X POST -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" -H "Idempotency-Key: weekly-2026-42" \
  -d '{"format":"csv","filter":{"status":"complete"}}' https://surveys.example.org/api/v1/surveys/$SURVEY/exports
curl -H "Authorization: Bearer $KEY" https://surveys.example.org/api/v1/exports/$EXPORT
curl -H "Authorization: Bearer $KEY" -o results.csv https://surveys.example.org/api/v1/exports/$EXPORT/file
```

Poll the export every few seconds until `status` is `ready` (then `file.url` is set), `failed` (then `error` is a
problem) or `expired`. The same `Idempotency-Key` within 24 hours gives the same export. Problems:
`unknown-format`, `not-published`, `too-many-exports`, `export-not-ready`, `export-expired` (see [errors.md](errors.md)).

## Request

In the Public API (the designer sends the same fields, with the filter at the top level):

```json
{ "format": "excel", "layout": "wide", "values": "labels", "language": "de", "filter": { "status": "complete", "from": "2026-10-01T00:00:00Z", "to": null, "version": null } }
```

- `format`: `excel`, `csv`, `spss`, `stata` or `json`.
- `layout` (CSV and Excel): `wide` (default, one row per response) or `long` (one row per answered variable: `response_id`, `variable`, `value`).
- `values` (CSV and Excel): `labels` (default), `codes`, or `both` (the code, and the label in a column `<variable>_label`).
- `status`, `from`, `to`, `version`: the results' filter; empty means all.
- `language`: the language of labels; the survey's default when it does not offer it.

Errors when starting: `400` `unknown-format`; `403` (designer: `role-not-allowed`; Public API: `scope-missing`);
`404` for a survey of another organisation; `409` `not-published`; `429` `too-many-exports`. An Excel export above
1,048,575 rows fails with the error `too-many-rows-for-excel`; choose CSV.

## Variables (the codebook)

Seven columns come first: `response_id`, `status` (`complete` or `partial`), `version`, `language`, `started_at`,
`completed_at` (ISO 8601, UTC) and `duration_seconds`. Then one variable per answer part, in the survey's order:

| Question type | Variables | Codes |
|---|---|---|
| Single choice, dropdown, yes/no | `VAR` | the option's code |
| "Other, please specify" | `VAR_OTHER` (the name set in the survey) | the typed text |
| Multiple choice | `VAR_<code>` per option | `1` chosen, `0` not chosen |
| Ranking | `VAR_<code>` per option | its place, `1` = first |
| Matrix | `VAR_<row>` per row | the option's code |
| Rating, NPS, slider, number | `VAR` | the number |
| Text, email, phone, URL, date | `VAR` | the text as entered |

"Don't know" is `-99`, labelled "Don't know"; SPSS declares it missing, Stata reads it as `.a`. Text blocks and images
have no variables. Characters other than letters and digits in a code become `_` in variable names.

## Formats

- **Excel** (`.xlsx`): sheet "Responses" with a frozen header row, numbers as numbers and text as text (never a formula);
  sheet "Variables" with name, label, type and value labels. Written by Askwerk's own writer.
- **CSV** (`.csv`): RFC 4180, UTF-8 with a byte order mark, comma, CRLF. Text starting with `=`, `+`, `-` or `@` gets a
  leading apostrophe so spreadsheets do not run it as a formula.
- **SPSS** (`-spss.zip`): `<name>.csv` (wide, codes, UTF-8, line breaks in text replaced by spaces) and `<name>.sps`, which
  reads it with `GET DATA` and sets types, widths, variable labels, value labels and missing values.
- **Stata** (`-stata.zip`): `<name>.csv` as for SPSS and `<name>.do`, which imports it and sets variable labels, value
  labels for whole-number codes and `.a` for "Don't know".
- **Askwerk JSON** (`.json`): `survey`, `exportedAt`, `language`, `variables` (name, label, type, question, values) and
  `responses` (id, status, version, language, startedAt, completedAt, and `values` by variable name, codes, numbers as
  numbers).
