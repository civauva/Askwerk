// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createMessagesClient } from "./messages";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

describe("createMessagesClient (AWK-17 step 6)", () => {
  it("lists, saves, previews and test-sends messages", async () => {
    const requests: Request[] = [];
    const answers = [
      Response.json({ items: [{ kind: "invitation", subject: { en: "Hi" }, body: { en: "{link}" }, answerInEmail: true, isDefault: true }] }),
      new Response(null, { status: 204 }),
      Response.json({ subject: "Hi", html: "<table></table>", text: "Hi" }),
      new Response(null, { status: 204 }),
    ];
    const client = createMessagesClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    const messages = await client.list();
    await client.save("invitation", { subject: { en: "Hi" }, body: { en: "{link}" }, answerInEmail: false });
    const preview = await client.preview("invitation", { surveyId: "s1", language: "en" });
    await client.sendTest("invitation", { surveyId: "s1", language: "en" });

    expect(messages[0]?.kind).toBe("invitation");
    expect(preview.html).toBe("<table></table>");
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/messages",
      "PUT /api/app/messages/invitation",
      "POST /api/app/messages/invitation/preview",
      "POST /api/app/messages/invitation/test",
    ]);
  });
});
