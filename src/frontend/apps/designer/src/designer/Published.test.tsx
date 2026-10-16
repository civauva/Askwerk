// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Designer } from "./Designer";
import { FakeParticipantsClient } from "../participants/testing";
import { FakeDesignerApi, sequentialIds } from "./testing";

afterEach(cleanup);

/** Publishes the sample draft and returns the published view. */
async function published(saveFile = vi.fn(), copied: string[] = []) {
  const view = render(
    <Designer
      api={new FakeDesignerApi()}
      surveyId="s1"
      initialLocale="en"
      newId={sequentialIds()}
      autosaveDelayMs={0}
      saveFile={saveFile}
      participants={new FakeParticipantsClient()}
      writeClipboard={(text) => {
        copied.push(text);
        return Promise.resolve();
      }}
    />,
  );
  fireEvent.click(await screen.findByRole("button", { name: "Publish" }));
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Publish with missing translations" }));
  const dialog = within(await screen.findByRole("dialog", { name: "Published. Responses are being collected." }));

  return { dialog, saveFile, copied, ...view };
}

describe("Published (AWK-7, W12 B): link, QR code, embed and PDF follow one address", () => {
  it("starts on the primary address", async () => {
    const { dialog, container } = await published();

    expect(dialog.getByRole("button", { name: /^Address / }).textContent).toContain("surveys.acme.at (primary)");
    expect(dialog.getByText("https://surveys.acme.at/s/kz26b7mq")).toBeTruthy();
    expect(dialog.getByRole("img", { name: "QR code for https://surveys.acme.at/s/kz26b7mq" })).toBeTruthy();
    expect(dialog.getByRole("link", { name: "Download PDF (with QR code)" }).getAttribute("href")).toBe("/api/app/surveys/s1/pdf?language=en&host=surveys.acme.at");
    expect((dialog.getByRole("textbox", { name: "Embed code" }) as HTMLTextAreaElement).value).toBe(
      '<script src="https://surveys.acme.at/s/embed.js" data-survey="kz26b7mq" data-lang="en" async></script>',
    );
    expect(dialog.getByText("Only on websites listed under Organisation → Domains.")).toBeTruthy();
    await expectNoAxeViolations(container);
  });

  it("switches link, QR code, embed code and PDF to another address", async () => {
    const { dialog } = await published();

    fireEvent.click(dialog.getByRole("button", { name: /^Address / }));
    fireEvent.click(await screen.findByRole("option", { name: "acme.askwerk.app" }));

    expect(dialog.getByText("https://acme.askwerk.app/s/kz26b7mq")).toBeTruthy();
    expect(dialog.getByRole("img", { name: "QR code for https://acme.askwerk.app/s/kz26b7mq" })).toBeTruthy();
    expect(dialog.getByRole("link", { name: "Download PDF (with QR code)" }).getAttribute("href")).toBe("/api/app/surveys/s1/pdf?language=en&host=acme.askwerk.app");
    expect((dialog.getByRole("textbox", { name: "Embed code" }) as HTMLTextAreaElement).value).toContain('src="https://acme.askwerk.app/s/embed.js"');
  });

  it("offers the embed code as an iframe too, and copies it", async () => {
    const { dialog, copied } = await published();

    fireEvent.click(dialog.getByRole("tab", { name: "iframe" }));
    fireEvent.click(dialog.getByRole("button", { name: "Copy embed code" }));

    expect(copied).toEqual([
      '<iframe src="https://surveys.acme.at/s/kz26b7mq?embed&lang=en" title="Customer satisfaction 2026" style="width:100%;height:720px;border:0" loading="lazy"></iframe>',
    ]);
  });

  it("downloads the QR code as SVG", async () => {
    const { dialog, saveFile } = await published();

    fireEvent.click(dialog.getByRole("button", { name: "Download SVG" }));

    expect(saveFile).toHaveBeenCalledWith("kz26b7mq-qr.svg", expect.any(Blob));
    const svg = await (saveFile.mock.calls[0]![1] as Blob).text();
    expect(svg).toMatch(/^<svg xmlns="http:\/\/www.w3.org\/2000\/svg"/);
    expect(dialog.getByRole("button", { name: "Download PNG" })).toBeTruthy();
  });
});

describe("Published (AWK-7, W12 B): invite", () => {
  it("opens the invitation from the published view", async () => {
    const { dialog } = await published();

    fireEvent.click(dialog.getByRole("button", { name: "Invite participants…" }));

    expect(screen.getByRole("dialog", { name: "Invite participants" })).toBeTruthy();
  });
});
