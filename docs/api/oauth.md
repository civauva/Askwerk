# OAuth 2.0 client credentials

For server-to-server integrations that prefer short-lived tokens to a long-lived key. Create a client in **Organisation → API → OAuth clients**: it gets a client id (`cl_…`) and a secret (`acs_…`, shown once), with the same access presets as keys (Read results, Full access). A client works for one year.

## Get a token

```bash
curl -X POST https://surveys.acme.at/api/v1/oauth/token \
  -u "cl_4b8…:acs_…" \
  -d grant_type=client_credentials \
  -d "scope=surveys:read responses:read"
```

The id and secret may also go in the body as `client_id` and `client_secret`. `scope` is optional; without it the token gets all of the client's scopes.

```json
{ "access_token": "awt_…", "token_type": "Bearer", "expires_in": 900, "scope": "surveys:read responses:read" }
```

Send the token like a key: `Authorization: Bearer awt_…`. Every `/api/v1` endpoint accepts both. Ask for a new token when it expires (after 15 minutes); there is no refresh token.

## Errors (RFC 6749)

| Status | `error` | When |
| --- | --- | --- |
| 400 | `invalid_request` | No `grant_type` |
| 400 | `unsupported_grant_type` | Anything but `client_credentials` |
| 400 | `invalid_scope` | A scope the client does not have |
| 401 | `invalid_client` | Unknown client, wrong secret, revoked or expired |

## Revoking

Revoking the client on the API page ends all its tokens at once. Calls made with a token are in the audit log with the client as the actor.
