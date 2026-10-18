# Connecting a CRM

A CRM usually does two things with Askwerk: it **sends people** to a survey, each with their customer number, and it
**gets their answers** back as soon as they are complete. Organisation → Connections → "Connect a system" sets up both
in three steps; this guide shows what your CRM's side does.

## 1. Connect it

Choose **Get answers into my system** and **Send people from my system**. Give the address that should receive the
answers (an https URL of your CRM), keep **Client ID and secret (OAuth)** and **Match people by External ID, then
email**. Askwerk shows the client ID, the client secret and the webhook secret once; keep them in your CRM's vault.

## 2. Send people

Add people to a survey with their number in your CRM as `externalId`. Someone with a known External ID gets the new
details and keeps their personal link, so an address change in the CRM never makes a second invitation.

```ts
import { Askwerk } from "@askwerk/sdk";

const askwerk = new Askwerk({ baseUrl: "https://surveys.example.org", clientId: "cl_…", clientSecret: process.env.ASKWERK_SECRET! });
const result = await askwerk.participants.invite(surveyId, {
  participants: [{ email: "lena@example.org", name: "Lena Gruber", externalId: "CRM-10442", fields: { segment: "Key account" } }],
});
// result.participants[0].link is Lena's personal link, e.g. to show it in the CRM.
```

`send: false` makes the links only, for your CRM to send them itself. Fields become placeholders in the invitation,
e.g. `{segment}`.

## 3. Get the answers

Askwerk posts `response.completed` to the address you gave, signed with the webhook secret. Check the signature, then
find the person by the External ID in the event and fetch the answers.

```ts
import { verifyWebhook } from "@askwerk/sdk";

if (!(await verifyWebhook(process.env.ASKWERK_WEBHOOK_SECRET!, request.headers["askwerk-signature"], body))) {
  return reply.code(401).send();
}
const event = JSON.parse(body); // { type: "response.completed", data: { surveyId, responseId, … } }
```

Answers by variable are in `GET /api/v1/surveys/{surveyId}/responses` (scope `responses:read`); see
[Responses](responses.md). Every delivery is listed under Connections, where a failing address shows at once.

## 4. Keep Contacts in step

To have the CRM's people in Askwerk's address book as well, for invitations from the designer, send each change with
`PUT /api/v1/contacts` (scope `contacts:write`). It finds the person by External ID, else email, and is safe to repeat;
someone who unsubscribed stays unsubscribed. See [Contacts](contacts.md).

```ts
await askwerk.contacts.put({ email: "lena@example.org", name: "Lena Gruber", externalId: "CRM-10442", groups: ["Key accounts"] });
```

## When something goes wrong

- **Calls refused** in Connections: the client was paused, its secret replaced, or it asked for something it may not do.
- **Not working**: every delivery to your address failed; Askwerk retries for 12 hours and pauses the webhook after 3
  days. Fix the address, then resume it under Connections.
