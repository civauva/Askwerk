<!-- SPDX-License-Identifier: Apache-2.0 -->
# Signing in from your product and provisioning users

Off by default. An owner switches them on in **Organisation → Sign-in**. Askwerk accounts stay on for owners, so
nobody is locked out when your product's sign-in fails. Every hand-over and every provisioning change is in the audit
log.

## Hand-over

Your product signs a short JSON Web Token for its signed-in user and sends the browser to Askwerk:

```
GET  https://surveys.example.org/handover?token=eyJ…
POST https://surveys.example.org/handover        (form field token)
```

| Claim | |
|---|---|
| `iss` | your issuer, as entered on the Sign-in page |
| `aud` | the audience the Sign-in page shows (`askwerk:…`) |
| `sub` | your user id; it binds the person to an Askwerk user |
| `email`, `name` | the person; `name` is optional |
| `groups` or `roles` | optional; mapped to Askwerk roles by "Roles from your system" |
| `exp` | at most 5 minutes ahead |
| `jti` | unique; a token works once |
| `return` | optional; a path in Askwerk to open, e.g. `/surveys` |

Sign with RS256 or ES256 (P-256). Paste your public key (PEM) on the Sign-in page, a second one while you rotate, or
give a JWKS URL (https). Askwerk checks signature, issuer, audience, expiry and that the `jti` is new; finds the person
by `sub`, or the first time by email, or creates them without an Askwerk password; gives the role of the first rule
whose group they are in (or the role for anyone else, or no access); and starts their session.

A bad, expired or used token shows "This sign-in link no longer works. Start again from your product." Someone no
rule lets in sees "Your account has no access to … Ask your administrator."

```js
import { createSign, randomUUID } from "node:crypto";
const b64 = (value) => Buffer.from(typeof value === "string" ? value : JSON.stringify(value)).toString("base64url");
const header = b64({ alg: "RS256", typ: "JWT" });
const payload = b64({ iss: "https://hr.example.org", aud: "askwerk:…", sub: user.id, email: user.email, name: user.name,
  groups: user.groups, exp: Math.floor(Date.now() / 1000) + 120, jti: randomUUID() });
const signature = createSign("RSA-SHA256").update(`${header}.${payload}`).sign(privateKey, "base64url");
res.redirect(`https://surveys.example.org/handover?token=${header}.${payload}.${signature}`);
```

## Single sign-on (OpenID Connect)

Entra ID, ADFS, Google, Okta and any other OpenID Connect provider. Register Askwerk at your provider as a web
application with the redirect URI the Sign-in page shows (`https://…/sso/callback/…`), then enter the provider's issuer
URL (https), the client id and, for a confidential client, the secret. Askwerk uses the authorization code flow with
PKCE and reads `sub`, `email`, `name` and `groups` (or `roles`) from the ID token; an `email_verified` of false is
refused.

People start at the link the Sign-in page shows, `https://…/sso/{organisation}`, or on Askwerk's sign-in page under
"Sign in with single sign-on" with the organisation's short name. The first time, a person is matched by email, then by
the provider's id. Roles come from the same "Roles from your system" as the hand-over.

## Directory (LDAP, Active Directory)

People sign in with their directory user name and password. Askwerk connects over LDAPS (TLS) only, with its own small
LDAP client:

1. the search account (a DN and its password, stored encrypted) binds and searches under the base DN for one entry
   with the object class and whose sign-in attribute equals the user name (`sAMAccountName` and `user` for Active
   Directory, `uid` and `person` for others);
2. that entry's DN binds with the person's password; an empty password is always refused;
3. `mail` is the address (required), `displayName` or `cn` the name, `entryUUID` or `objectGUID` the id, and the group
   attribute (`memberOf` by default) gives the groups for "Roles from your system": both the full value and, for a DN,
   its CN (`cn=HR-Admins,ou=groups,…` matches `HR-Admins`).

Passwords are never stored. After five wrong passwords for one user name, signing in pauses for 15 minutes. A server
whose certificate comes from your own CA needs that CA's certificate (PEM) under More settings. "Test connection"
checks the saved settings with a test person and shows the role they would get.

People choose "Sign in with single sign-on" on Askwerk's sign-in page and type the organisation's short name; the
directory's form follows. The link `https://…/sso/{organisation}` opens it directly.

## Provisioning API

With Provisioning switched on, your system keeps Askwerk's users in step with its own, using an API key or OAuth
client with the scope `users:manage` (never part of a preset; tick it under More on the API page).

| Method | Path | |
|---|---|---|
| PUT | `/api/v1/users/{externalId}` | create or change: `{ "email", "name", "role", "active" }`; 201 when new |
| GET | `/api/v1/users?cursor=&limit=` | the provisioned users, oldest first |
| DELETE | `/api/v1/users/{externalId}` | deactivate: out of the organisation, sessions ended; never deleted |

`role` is `admin`, `editor`, `analyst` or `viewer`; owners are never provisioned and an owner's role never changes.
`externalId` is the same id as `sub` in a hand-over. On the Members page, users your system manages show "Managed by
…" (the name on the Sign-in page); their role and removal are locked there. Their responses keep their person after
deactivation.

| Problem | Status | |
|---|---|---|
| `provisioning-off` | 409 | An owner switches Provisioning on first. |
| `user-invalid` | 400 | email, name or role missing or wrong. |
| `owner-not-provisioned` | 400 | Choose admin or a lower role. |
| `user-not-found` | 404 | No provisioned user with this id. |
