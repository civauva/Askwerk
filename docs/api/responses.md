<!-- SPDX-License-Identifier: Apache-2.0 -->
# Responses and the codebook

Two read endpoints for keeping a copy of the responses up to date, for example in a reporting database. Both need
the scope `responses:read`.

## Responses changed since a sequence

```sh
curl -H "Authorization: Bearer $KEY" "https://surveys.example.org/api/v1/surveys/$SURVEY/responses?since=0&limit=200&language=de"
```

```json
{
  "items": [{
    "id": "…", "status": "complete", "version": 3, "language": "de",
    "participant": { "email": "ana@example.org", "externalId": null },
    "startedAt": "…", "completedAt": "…", "changeSequence": 81234,
    "answers": [
      { "variable": "SAT", "value": "4", "label": "Zufrieden" },
      { "variable": "SOURCE_2", "value": "1", "label": "Werbung" }
    ]
  }],
  "nextSince": 81234
}
```

- Every new answer, change and completion gives a response a higher `changeSequence`. Store `nextSince` and pass it
  as `since` next time: you get each new or changed response once, and never miss one. When nothing changed,
  `items` is empty and `nextSince` stays the same.
- Responses come oldest change first, at most `limit` (200 when not given, at most 500). Ask again with the new
  `since` until `items` is empty.
- A changed response comes again in full; replace what you stored for its `id`.
- `answers` is in long format, one entry per variable of the codebook that has an answer: matrix rows are
  `QUESTION_ROW`, multiple choice gives `QUESTION_CODE` with value `1` for each chosen option (unchosen options are
  left out), and open questions give their text. `label` is the value's label in `language` (the survey's default
  language when not given).
- `participant.email` is set for personal invitations; `externalId` stays `null` until a host system signs people in.

## The codebook

```sh
curl -H "Authorization: Bearer $KEY" "https://surveys.example.org/api/v1/surveys/$SURVEY/codebook?language=de"
```

```json
{
  "version": 3, "language": "de",
  "variables": [
    { "variable": "SAT", "label": "Wie zufrieden sind Sie?", "numeric": true,
      "values": [{ "code": "1", "label": "Gar nicht" }, { "code": "5", "label": "Sehr" }] }
  ]
}
```

The codebook of the latest published version, with the same variables as the responses and every export. A survey
never published answers `409 not-published`.

An example that copies both into SQL Server is in [`docs/integrations/sql-server-sync`](../integrations/sql-server-sync/).
