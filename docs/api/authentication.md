<!-- SPDX-License-Identifier: Apache-2.0 -->
# Authentication

The Public API (`/api/v1`) is called with an API key. Owners and admins create keys in the designer under
Organisation → API. A key belongs to its organisation and sees only its surveys and results.

```sh
curl -H "Authorization: Bearer awk_7Hq2xR…" https://surveys.example.org/api/v1/surveys
```

- **Shown once.** The whole key is shown when it is created; Askwerk keeps only a hash of it. The list shows its
  first eight characters, to tell keys apart. Keep a key like a password.
- **Permissions (scopes).** "Read results" (the default) has `surveys:read` and `responses:read`: list and read
  surveys, start and download exports. "Full access" has every scope a key may have, also for endpoints added later.
  No key can manage users, roles or settings.
- **Expiry.** 30 days, 90 days (the default), 1 year or never. An expired or revoked key gets `401` at once.
- **Audit.** Exports, downloads and every change made with a key are in the organisation's audit log, with the key as
  actor. The list shows when a key was last used.
- **Rate limit.** 600 calls a minute per key. Every answer carries `RateLimit-Limit`, `RateLimit-Remaining` and
  `RateLimit-Reset` (seconds); above the limit the answer is `429 rate-limited` with `Retry-After`.
- **Sessions.** The Public API does not accept the designer's session cookie, and the designer's App API does not
  accept API keys.

The contract is [openapi.json](openapi.json); errors are described in [errors.md](errors.md).
