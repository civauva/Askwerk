<!-- SPDX-License-Identifier: Apache-2.0 -->
# SQL Server sync

An example to copy and adapt: a small .NET console job that copies responses from Askwerk into SQL Server tables, in
long format. It reads the [responses and codebook endpoints](../../api/responses.md) with an API key; Askwerk never
connects to your database.

| File | What it is |
|---|---|
| `create-tables.sql` | the five tables; run it once in your database |
| `appsettings.json` | the Askwerk address, key, label language, surveys, and the connection string |
| `Sync.cs` | the copying: codebook, then responses page by page |
| `Program.cs` | reads the settings, copies each survey, logs a line per survey |
| `Dockerfile` | to run it as a container |

## Set up

1. In Askwerk, Organisation → API: create a key with `surveys:read` and `responses:read`.
2. Run `create-tables.sql` in your database.
3. Fill in `appsettings.json`, or set `ASKWERK_BASE_URL`, `ASKWERK_API_KEY` and `SQLSERVER_CONNECTION_STRING`.
4. `dotnet run`, then schedule it: Task Scheduler, cron, or the container, nightly or every 15 minutes.

```sh
docker build -t askwerk-sql-sync .
docker run --rm -e ASKWERK_BASE_URL=https://surveys.example.org -e ASKWERK_API_KEY=awk_… \
  -e SQLSERVER_CONNECTION_STRING="Server=…;Database=Reporting;User Id=…;Password=…;Encrypt=true" askwerk-sql-sync
```

## How it works

For each survey (those in `Surveys`, or every published one):

1. The codebook is stored in `askwerk_variable` and `askwerk_value_label`.
2. Responses changed since `askwerk_sync_state.last_sequence` are read page by page. Each page is one transaction:
   the responses are merged into `askwerk_response`, their answers replaced in `askwerk_answer`, and the new sequence
   stored. A crash repeats at most one page; running it again changes nothing.
3. One log line per survey. The exit code is 0, or 1 when a survey failed; its error is in `askwerk_sync_state.last_error`.

Reports join `askwerk_answer` to `askwerk_value_label` on survey, variable and value. A new question in a later
version adds rows, never columns.

```sql
SELECT r.completed_at, a.variable, a.value, l.label
FROM askwerk_answer a
JOIN askwerk_response r ON r.response_id = a.response_id
LEFT JOIN askwerk_value_label l ON l.survey_id = r.survey_id AND l.variable = a.variable AND l.code = a.value AND l.language = 'de'
WHERE r.status = 'complete';
```

The only package is Microsoft.Data.SqlClient (MIT). The example is tested against SQL Server 2022 and a running
Askwerk in Askwerk's full test run.
