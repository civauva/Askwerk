# Licensing

Askwerk uses two open-source licences, so that the product is protected against closed hosted copies while anyone can integrate with it without taking on copyleft obligations.

## Which folder has which licence

| Licence | Folders | What it covers |
|---|---|---|
| **AGPL-3.0-only** (root [`LICENSE`](LICENSE)) | everything not listed below, including `src/backend/`, `src/frontend/apps/`, `src/frontend/packages/ui`, `src/frontend/packages/telemetry`, `src/frontend/packages/app-client`, `deploy/`, `bin/` | the product itself: server, apps, design system, deployment |
| **Apache-2.0** (a `LICENSE` file in each folder) | `src/frontend/packages/schema`, `src/frontend/packages/embed`, `src/sdks/`, `docs/api/`, `docs/integrations/` | what integrators put into their own systems: the survey JSON schema and expression evaluator, the embed script, the generated SDKs, the Public API description, example integrations |

Every source file states its licence in an `SPDX-License-Identifier` header; CI fails when one is missing. The MCP server is part of the API and therefore AGPL-3.0; only example client code is Apache-2.0.

## What this means for you
- **Self-hosting Askwerk**, for yourself or for customers: allowed. If you modify Askwerk and let others use it over a network, the AGPL requires you to offer them the source of your modified version.
- **Integrating with Askwerk** through the Public API, webhooks, the embed script or the SDKs: allowed under Apache-2.0, with no obligation to open your own code.
- **Building a product on top of the AGPL parts**: your combined work falls under the AGPL.

This is a summary, not legal advice; the licence texts are what count.

## Dependencies
Dependencies of AGPL code must be AGPL-compatible; dependencies of the Apache-2.0 folders must be permissive only, so integrators inherit nothing. The exact rules are in [`agents/decisions.md`](agents/decisions.md).

## Contributions
Contributions are accepted under the licence of the folder they change, with a [Developer Certificate of Origin](https://developercertificate.org/) sign-off on every commit (`git commit -s`). There is no CLA, so the licences cannot be changed later without every contributor's consent. See [CONTRIBUTING.md](CONTRIBUTING.md).
