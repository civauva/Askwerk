# Webhooks

Askwerk posts an event to your server the moment it happens. Webhooks are managed in **Organisation → API → Webhooks** or through the Public API with a key that has the scope `webhooks:manage`.

## Manage

| Method | Path | What |
| --- | --- | --- |
| GET | `/api/v1/webhooks` | The organisation's webhooks, newest first, each with its last delivery |
| POST | `/api/v1/webhooks` | Create one: `{ "url", "events", "description"? }` → 201 with `secret`, shown only here |
| PATCH | `/api/v1/webhooks/{id}` | Change `url`, `events` or `active`; `active: true` also resumes a paused webhook |
| DELETE | `/api/v1/webhooks/{id}` | Delete it and its deliveries log |
| GET | `/api/v1/webhooks/{id}/deliveries?cursor=&limit=` | Its deliveries, newest first; pass `nextCursor` as `cursor` |
| POST | `/api/v1/webhooks/{id}/deliveries/{deliveryId}/replay` | Send that event again (202) |

URLs must be https (http://localhost works only on a development instance). At most 20 webhooks per organisation. Errors are problem details: `url-invalid`, `events-invalid`, `webhooks-too-many`, `webhook-not-found`, `delivery-not-found`.

```bash
curl -X POST https://surveys.acme.at/api/v1/webhooks \
  -H "Authorization: Bearer $ASKWERK_KEY" -H "Content-Type: application/json" \
  -d '{"url":"https://hr.acme.at/askwerk","events":["response.completed","export.completed"]}'
```

## Delivery

```
POST https://hr.acme.at/askwerk
Content-Type: application/json
Askwerk-Event: response.completed
Askwerk-Delivery: dlv_0199…
Askwerk-Signature: t=1760518800,v1=5e2c…

{ "id": "evt_0199…", "type": "response.completed", "occurredAt": "2026-10-15T09:00:00Z",
  "tenantId": "…", "data": { … } }
```

- Any 2xx within 10 seconds counts as delivered. Otherwise Askwerk tries again after 1 minute, 5 minutes, 30 minutes, 2 hours, 6 hours and 12 hours, then gives up.
- Delivery is at least once: deduplicate by the event `id` (a replay keeps it).
- Events are queued together with the change that causes them, so nothing is sent for a change that did not happen.
- A webhook whose deliveries have failed for 3 days is paused; Resume on the API page (or `PATCH active: true`) starts it again.

## Checking the signature

`v1` is HMAC-SHA256 with the webhook's secret over `"{t}.{body}"`, as lower-case hex. Reject a request whose `t` is more than 5 minutes old.

```js
// Node
import { createHmac, timingSafeEqual } from "node:crypto";
function verify(secret, header, body) {
  const { t, v1 } = Object.fromEntries(header.split(",").map((part) => part.split("=")));
  const expected = createHmac("sha256", secret).update(`${t}.${body}`).digest("hex");
  return Math.abs(Date.now() / 1000 - Number(t)) < 300 && timingSafeEqual(Buffer.from(v1), Buffer.from(expected));
}
```

```csharp
// C#
static bool Verify(string secret, string header, string body)
{
    var parts = header.Split(',').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => part[1]);
    var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{parts["t"]}.{body}")));
    return Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - long.Parse(parts["t"])) < 300
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts["v1"]), Encoding.UTF8.GetBytes(expected));
}
```

```python
# Python
import hashlib, hmac, time
def verify(secret: str, header: str, body: str) -> bool:
    parts = dict(part.split("=", 1) for part in header.split(","))
    expected = hmac.new(secret.encode(), f"{parts['t']}.{body}".encode(), hashlib.sha256).hexdigest()
    return abs(time.time() - int(parts["t"])) < 300 and hmac.compare_digest(parts["v1"], expected)
```

## Events

| Type | When | `data` |
| --- | --- | --- |
| `response.completed` | A respondent sent a response | `surveyId`, `responseId`, `version`, `completedAt` |
| `export.completed` | An export is ready | `exportId`, `surveyId`, `format`, `file`: `name`, `size`, `url` (download with a key that has `responses:read`) |
| `survey.published` | A version was published | `surveyId`, `version` |
| `survey.closed` | A survey stopped taking responses | `surveyId` |
| `ping` | Test on the API page | `webhookId` |

More events (`response.started`, `response.updated`, `participant.created`, `import.completed`) come when their features are in the Public API. New event types and new fields are additive; ignore what you do not know.
