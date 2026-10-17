// SPDX-License-Identifier: AGPL-3.0-only

/** The languages Connect a system shows code in (W36). */
export const sampleLanguages = ["csharp", "typescript", "python", "curl", "postman"] as const;

export type SampleLanguage = (typeof sampleLanguages)[number];

export const sampleNames: Record<SampleLanguage, string> = { csharp: "C#", typescript: "TypeScript", python: "Python", curl: "cURL", postman: "Postman" };

export type SampleSetup = {
  /** This Askwerk's address, e.g. https://surveys.example.org. */
  baseUrl: string;
  signIn: "oauth" | "api-key" | null;
  /** The OAuth client id; public, so it may be shown. Secrets never are: the code reads them from the environment. */
  clientId: string | null;
  /** The system sends people. */
  people: boolean;
  /** The system reads answers. */
  results: boolean;
  /** Askwerk posts answers to the system. */
  webhook: boolean;
};

/**
 * The code a system runs to work with its connection, with the SDKs of src/sdks: sign in, add a person with their
 * External ID or read answers, and check a webhook's signature. Secrets come from environment variables.
 */
export function sampleOf(language: SampleLanguage, setup: SampleSetup): string {
  const { baseUrl, signIn, clientId, people, results, webhook } = setup;
  const id = clientId ?? "cl_…";
  const lines: string[] = [];

  switch (language) {
    case "csharp":
      lines.push("// dotnet add package Askwerk.Sdk", "using Askwerk.Sdk;", "");
      if (signIn !== null) {
        lines.push(
          signIn === "oauth"
            ? `using var askwerk = new AskwerkClient(new Uri("${baseUrl}"), AskwerkAuth.ClientCredentials("${id}", Environment.GetEnvironmentVariable("ASKWERK_SECRET")!));`
            : `using var askwerk = new AskwerkClient(new Uri("${baseUrl}"), AskwerkAuth.ApiKey(Environment.GetEnvironmentVariable("ASKWERK_KEY")!));`,
        );
      }
      if (people) {
        lines.push("", "// Add a person to a survey with their number in your system; known numbers get the new details", 'await askwerk.Participants.InviteAsync(surveyId, new PublicInviteRequest { Participants = [new PublicPerson { Email = "lena@example.org", ExternalId = "CRM-10442" }] });');
      }
      if (results) {
        lines.push("", "// Read the answers since the last run", "await foreach (var response in askwerk.Responses.ListAllAsync(surveyId, since: lastSequence)) { Store(response); }");
      }
      if (webhook) {
        lines.push("", "// In your webhook: check the signature, then use the answer", 'if (!AskwerkWebhooks.Verify(webhookSecret, request.Headers["Askwerk-Signature"]!, body)) return Results.Unauthorized();');
      }
      break;
    case "typescript":
      lines.push("// npm install @askwerk/sdk", 'import { Askwerk, verifyWebhook } from "@askwerk/sdk";', "");
      if (signIn !== null) {
        lines.push(
          signIn === "oauth"
            ? `const askwerk = new Askwerk({ baseUrl: "${baseUrl}", clientId: "${id}", clientSecret: process.env.ASKWERK_SECRET! });`
            : `const askwerk = new Askwerk({ baseUrl: "${baseUrl}", apiKey: process.env.ASKWERK_KEY! });`,
        );
      }
      if (people) {
        lines.push("", "// Add a person to a survey with their number in your system; known numbers get the new details", 'await askwerk.participants.invite(surveyId, { participants: [{ email: "lena@example.org", externalId: "CRM-10442" }] });');
      }
      if (results) {
        lines.push("", "// Read the answers since the last run", "for await (const response of askwerk.responses.listAll(surveyId, { since: lastSequence })) store(response);");
      }
      if (webhook) {
        lines.push("", "// In your webhook: check the signature, then use the answer", 'if (!(await verifyWebhook(process.env.ASKWERK_WEBHOOK_SECRET!, request.headers["askwerk-signature"], body))) return reply.code(401).send();');
      }
      break;
    case "python":
      lines.push("# pip install askwerk", "import os", "from askwerk import Askwerk, verify_webhook", "");
      if (signIn !== null) {
        lines.push(
          signIn === "oauth"
            ? `askwerk = Askwerk("${baseUrl}", client_id="${id}", client_secret=os.environ["ASKWERK_SECRET"])`
            : `askwerk = Askwerk("${baseUrl}", api_key=os.environ["ASKWERK_KEY"])`,
        );
      }
      if (people) {
        lines.push("", "# Add a person to a survey with their number in your system; known numbers get the new details", 'askwerk.participants.invite(survey_id, {"participants": [{"email": "lena@example.org", "externalId": "CRM-10442"}]})');
      }
      if (results) {
        lines.push("", "# Read the answers since the last run", "for response in askwerk.responses.list_all(survey_id, since=last_sequence):", "    store(response)");
      }
      if (webhook) {
        lines.push("", "# In your webhook: check the signature, then use the answer", 'if not verify_webhook(os.environ["ASKWERK_WEBHOOK_SECRET"], request.headers["Askwerk-Signature"], body):', "    return 401");
      }
      break;
    case "curl":
      if (signIn === "oauth") {
        lines.push("# A token for 15 minutes", `TOKEN=$(curl -s ${baseUrl}/api/v1/oauth/token -d grant_type=client_credentials -d client_id=${id} -d client_secret=$ASKWERK_SECRET | jq -r .access_token)`, "");
      } else if (signIn === "api-key") {
        lines.push("TOKEN=$ASKWERK_KEY", "");
      }
      if (people) {
        lines.push("# Add a person to a survey with their number in your system", `curl ${baseUrl}/api/v1/surveys/$SURVEY/participants \\`, '  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \\', `  -d '{"participants":[{"email":"lena@example.org","externalId":"CRM-10442"}]}'`);
      }
      if (results) {
        lines.push(...(people ? [""] : []), "# Read the answers", `curl -H "Authorization: Bearer $TOKEN" ${baseUrl}/api/v1/surveys/$SURVEY/responses`);
      }
      if (webhook) {
        lines.push(...(people || results ? [""] : []), "# Webhooks are signed: Askwerk-Signature: t=…,v1=hex(HMAC-SHA256(secret, \"t.body\"))");
      }
      break;
    default:
      lines.push(
        "Postman → Import → Link:",
        `  ${baseUrl}/api/v1/openapi.json`,
        "",
        signIn === "oauth"
          ? `Authorization: OAuth 2.0, grant type Client Credentials, token URL ${baseUrl}/api/v1/oauth/token, client ID ${id}, client secret from your vault.`
          : "Authorization: Bearer Token, with the API key from your vault.",
      );
  }

  return lines.join("\n").replace(/^\n+/, "");
}
