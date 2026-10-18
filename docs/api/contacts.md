<!-- SPDX-License-Identifier: Apache-2.0 -->
# Contacts

Contacts are the organisation's address book: people with their External ID, language, fields and groups, invited
to many surveys. A CRM or HR system keeps them in step with four calls. Reading needs the scope `contacts:read`;
adding, changing and removing need `contacts:write`. "Full access" has both.

## Add or update a person

One call, whether the person is new or known. Askwerk finds them by `externalId` first, else by `email`, so an
address change in your system never makes a second contact. Sending the same person again changes nothing.

```sh
curl -X PUT -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  https://surveys.example.org/api/v1/contacts \
  -d '{ "email": "lena@example.org", "name": "Lena Gruber", "language": "de", "externalId": "CRM-10442",
        "groups": ["Key accounts"], "fields": { "segment": "Gold" } }'
```

```json
{
  "id": "0199f1c2-…", "email": "lena@example.org", "name": "Lena Gruber", "externalId": "CRM-10442",
  "language": "de", "groups": ["Key accounts"], "fields": { "segment": "Gold" },
  "unsubscribed": false, "bounced": false,
  "createdAt": "2026-10-18T09:12:00Z", "updatedAt": "2026-10-18T09:12:00Z"
}
```

- `201` for a new contact (with `Location`), `200` for a known one.
- What you do not send is kept: an empty `name`, `language` or `externalId` keeps the stored one. `fields` and
  `groups`, when sent, replace the stored ones.
- `unsubscribed` and `bounced` are read only. Someone who unsubscribed stays unsubscribed, also when your system gives
  them a new address; the API never subscribes anyone again.
- `400 contact-invalid` names each field in `errors`, e.g. `{ "email": ["email-invalid"] }`. `409 contact-email-taken`:
  the External ID found one contact and the email another; change or remove one of them first.

## List and read

```sh
curl -H "Authorization: Bearer $KEY" "https://surveys.example.org/api/v1/contacts?group=Key%20accounts&limit=100"
curl -H "Authorization: Bearer $KEY" "https://surveys.example.org/api/v1/contacts?externalId=CRM-10442"
curl -H "Authorization: Bearer $KEY" https://surveys.example.org/api/v1/contacts/0199f1c2-…
```

`search` finds part of a name, email or External ID; `group` and `externalId` narrow the list. Contacts come oldest
first, up to 100 a page: pass `nextCursor` as `cursor` until it is `null`.

## Remove

```sh
curl -X DELETE -H "Authorization: Bearer $KEY" https://surveys.example.org/api/v1/contacts/0199f1c2-…
```

`204`; `404 contact-not-found` when there is no such contact. Their participants in surveys stay, without the link to
the contact, and an unsubscribed address stays unsubscribed.

Every change is in the organisation's audit log (`contact.added`, `contact.changed`, `contact.removed`) with the key
or OAuth client as actor. The SDKs have the same calls as `contacts.list`, `listAll`, `get`, `put` and `delete`.
