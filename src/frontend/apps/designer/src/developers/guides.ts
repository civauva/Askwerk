// SPDX-License-Identifier: AGPL-3.0-only
// The guides of the Developers hub (AWK-34): the documents in docs/, bundled with the app so they match this version.
import authentication from "../../../../../../docs/api/authentication.md?raw";
import crm from "../../../../../../docs/api/connect-a-crm.md?raw";
import embed from "../../../../../../docs/api/embed.md?raw";
import errors from "../../../../../../docs/api/errors.md?raw";
import exports from "../../../../../../docs/api/exports.md?raw";
import identity from "../../../../../../docs/api/identity.md?raw";
import mcp from "../../../../../../docs/api/mcp.md?raw";
import oauth from "../../../../../../docs/api/oauth.md?raw";
import responses from "../../../../../../docs/api/responses.md?raw";
import webhooks from "../../../../../../docs/api/webhooks.md?raw";
import sqlServerSync from "../../../../../../docs/integrations/sql-server-sync/README.md?raw";
import type { DevelopersMessageKey } from "./messages";

export type Guide = {
  readonly id: string;
  readonly title: DevelopersMessageKey;
  readonly tag: DevelopersMessageKey;
  readonly summary: DevelopersMessageKey;
  readonly source: string;
};

export const guides: readonly Guide[] = [
  { id: "crm", title: "guideCrm", tag: "tagDataIn", summary: "guideCrmText", source: crm },
  { id: "responses", title: "guideResponses", tag: "tagDataOut", summary: "guideResponsesText", source: responses },
  { id: "exports", title: "guideExports", tag: "tagDataOut", summary: "guideExportsText", source: exports },
  { id: "webhooks", title: "guideWebhooks", tag: "tagEvents", summary: "guideWebhooksText", source: webhooks },
  { id: "provisioning", title: "guideProvisioning", tag: "tagDataIn", summary: "guideProvisioningText", source: identity },
  { id: "sign-in", title: "guideSignIn", tag: "tagIdentity", summary: "guideSignInText", source: `${identity}\n\n${oauth}` },
  { id: "embed", title: "guideEmbed", tag: "tagEmbed", summary: "guideEmbedText", source: embed },
  { id: "sql-server", title: "guideSqlServer", tag: "tagExample", summary: "guideSqlServerText", source: sqlServerSync },
  { id: "mcp", title: "guideMcp", tag: "tagAi", summary: "guideMcpText", source: mcp },
  { id: "authentication", title: "guideAuthentication", tag: "tagIdentity", summary: "guideAuthenticationText", source: `${authentication}\n\n${errors}` },
];

/** The example addresses in the documents become this Askwerk's. */
export function withBaseUrl(source: string, baseUrl: string): string {
  return source.replace(/https:\/\/surveys\.(example\.org|acme\.at)/g, baseUrl);
}
