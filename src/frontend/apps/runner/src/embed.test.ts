// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { withEmbedEvents } from "./embed";
import { FakeRunClient } from "./testing";

describe("withEmbedEvents (AWK-7): the host page learns what happens, never the answers", () => {
  it("posts started, page and completed", async () => {
    const posted: unknown[] = [];
    const client = withEmbedEvents(new FakeRunClient(), "kz26b7mq", (message) => posted.push(message));

    await client.start("s1", "de");
    await client.savePage("s1", "p1", { q1: { number: 4 } });
    await client.complete("s1");

    expect(posted).toEqual([
      { type: "askwerk:started", survey: "kz26b7mq", language: "de" },
      { type: "askwerk:page", survey: "kz26b7mq", page: "p1" },
      { type: "askwerk:completed", survey: "kz26b7mq" },
    ]);
  });
});
