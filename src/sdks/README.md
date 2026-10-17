<!-- SPDX-License-Identifier: Apache-2.0 -->
# Askwerk SDKs

The Askwerk Public API for TypeScript, C# and Python. No dependencies: each uses its standard library's HTTP client.

| Language | Package | Needs |
|---|---|---|
| TypeScript / JavaScript | `@askwerk/sdk` (npm) | Node 20+, or any runtime with `fetch` |
| C# | `Askwerk.Sdk` (NuGet) | .NET 8+ |
| Python | `askwerk` (PyPI) | Python 3.11+ |

## Authentication

An API key (Organisation → API), or an OAuth client: the SDK then fetches access tokens itself and renews them before
they run out.

```ts
import { Askwerk } from "@askwerk/sdk";
const askwerk = new Askwerk({ baseUrl: "https://surveys.example.org", apiKey: process.env.ASKWERK_KEY! });
// or: new Askwerk({ baseUrl, clientId: "cl_…", clientSecret: process.env.ASKWERK_SECRET! })
```

```csharp
using Askwerk.Sdk;
using var askwerk = new AskwerkClient(new Uri("https://surveys.example.org"), AskwerkAuth.ApiKey(key));
// or: AskwerkAuth.ClientCredentials("cl_…", secret)
```

```python
from askwerk import Askwerk
askwerk = Askwerk("https://surveys.example.org", api_key=os.environ["ASKWERK_KEY"])
# or: Askwerk(base_url, client_id="cl_…", client_secret=os.environ["ASKWERK_SECRET"])
```

## What you can do

| | TypeScript | C# | Python |
|---|---|---|---|
| Surveys | `surveys.list`, `listAll`, `get` | `Surveys.ListAsync`, `ListAllAsync`, `GetAsync` | `surveys.list`, `list_all`, `get` |
| Responses since a sequence | `responses.list`, `listAll` | `Responses.ListAsync`, `ListAllAsync` | `responses.list`, `list_all` |
| Codebook | `responses.codebook` | `Responses.CodebookAsync` | `responses.codebook` |
| Exports | `exports.start`, `get`, `waitUntilReady`, `download` | `Exports.StartAsync`, `GetAsync`, `WaitUntilReadyAsync`, `DownloadAsync` | `exports.start`, `get`, `wait_until_ready`, `download` |
| Webhooks | `webhooks.list`, `create`, `update`, `delete`, `deliveries`, `deliveriesAll`, `replay` | the same, with `Async` | the same, in snake_case |
| Participants (participants:read, participants:write) | `participants.list`, `listAll`, `invite` | `Participants.ListAsync`, `ListAllAsync`, `InviteAsync` | `participants.list`, `list_all`, `invite` |
| Provisioned users (users:manage) | `users.list`, `listAll`, `put`, `deactivate` | `Users.ListAsync`, `ListAllAsync`, `PutAsync`, `DeactivateAsync` | `users.list`, `list_all`, `put`, `deactivate` |
| Webhook signatures | `verifyWebhook(secret, header, body)` | `AskwerkWebhooks.Verify(secret, header, body)` | `verify_webhook(secret, header, body)` |

```ts
const exp = await askwerk.exports.start(surveyId, { format: "csv" });
const ready = await askwerk.exports.waitUntilReady(exp.id);
const file = await askwerk.exports.download(ready.id);

for await (const response of askwerk.responses.listAll(surveyId, { since: lastSequence })) {
  store(response); // lastSequence = response.changeSequence
}
```

- The `…All` variants walk every page: by cursor for lists, by change sequence for responses.
- Errors are one type (`AskwerkError`, `AskwerkException`) with the HTTP status, the problem `type`, `title`,
  `detail` and `traceId`; see [errors](../../docs/api/errors.md).
- A `429` is retried up to three times, as long as `Retry-After` says. Starting an export sends an `Idempotency-Key`,
  so a retry never starts a second one.

Every call is described in the [Public API reference](../../docs/api/openapi.json).

## For contributors

The typed part of each SDK (models and calls) is generated from `docs/api/openapi.json` by `node src/sdks/generate.mjs`,
which needs no package; a test fails when it is stale. The rest (transport, authentication, errors, waiting and
signatures) is written by hand next to it. The full test run runs the same scenario in all three against a running
Askwerk. `src/sdks/release.sh` builds the three packages into `artifacts/sdks/`; publishing them is a manual step.
