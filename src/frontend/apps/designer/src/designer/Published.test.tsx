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

type Within = ReturnType<typeof within>;

/** The PDF link for a language, from the "Download PDF ▾" menu (W34). */
function pdfLink(dialog: Within, language = "English") {
  fireEvent.click(dialog.getByRole("button", { name: /^Download PDF/ }));

  return screen.getByRole("menuitem", { name: `PDF in ${language}` }).getAttribute("href");
}

/** The embed code, from the "Get embed code" dialog (W34). */
function embedCode(dialog: Within) {
  fireEvent.click(dialog.getByRole("button", { name: "Get embed code" }));
  const code = within(screen.getByRole("dialog", { name: "Embed code" }));

  return { code, value: () => (code.getByRole("textbox", { name: "Embed code" }) as HTMLTextAreaElement).value };
}

describe("Published (AWK-7, W12 B): link, QR code, embed and PDF follow one address", () => {
  it("starts on the primary address, with the link first and the three other ways as cards (W34)", async () => {
    const { dialog, container } = await published();

    expect(dialog.getByRole("button", { name: /^Address / }).textContent).toContain("surveys.acme.at (primary)");
    expect(dialog.getByText("https://surveys.acme.at/s/kz26b7mq")).toBeTruthy();
    expect(dialog.getByText("Anyone with the link can answer. Respondents choose their language in the survey.")).toBeTruthy();
    expect(dialog.getByRole("img", { name: "QR code for https://surveys.acme.at/s/kz26b7mq" })).toBeTruthy();
    expect(dialog.getAllByRole("heading", { level: 2 }).filter((heading) => heading.classList.contains("designer-share__title")).map((heading) => heading.textContent)).toEqual(["Link", "Invite by email", "On a website", "On paper"]);
    expect(dialog.getByText("Embed the survey in a page of your own site.")).toBeTruthy();
    await expectNoAxeViolations(container);
    expect(pdfLink(dialog)).toBe("/api/app/surveys/s1/pdf?language=en&host=surveys.acme.at");
    const { code, value } = embedCode(dialog);
    expect(value()).toBe('<script src="https://surveys.acme.at/s/embed.js" data-survey="kz26b7mq" async></script>');
    expect(code.getByText("Only on websites listed under Organisation → Domains.")).toBeTruthy();
  });

  it("switches link, QR code, embed code and PDF to another address", async () => {
    const { dialog } = await published();

    fireEvent.click(dialog.getByRole("button", { name: /^Address / }));
    fireEvent.click(await screen.findByRole("option", { name: "acme.askwerk.app" }));

    expect(dialog.getByText("https://acme.askwerk.app/s/kz26b7mq")).toBeTruthy();
    expect(dialog.getByRole("img", { name: "QR code for https://acme.askwerk.app/s/kz26b7mq" })).toBeTruthy();
    expect(pdfLink(dialog)).toBe("/api/app/surveys/s1/pdf?language=en&host=acme.askwerk.app");
    expect(embedCode(dialog).value()).toContain('src="https://acme.askwerk.app/s/embed.js"');
  });

  it("opens in the respondent's choice, or in a set language for link, QR code, embed code and PDF (W34)", async () => {
    const { dialog } = await published();
    const opensIn = within(dialog.getByRole("group", { name: "Opens in" }));

    expect(opensIn.getByRole("button", { name: "Respondent's choice" }).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(opensIn.getByRole("button", { name: "DE" }));

    expect(dialog.getByText("https://surveys.acme.at/s/kz26b7mq?lang=de")).toBeTruthy();
    expect(dialog.getByRole("img", { name: "QR code for https://surveys.acme.at/s/kz26b7mq?lang=de" })).toBeTruthy();
    expect(pdfLink(dialog, "German")).toBe("/api/app/surveys/s1/pdf?language=de&host=surveys.acme.at");
    expect(embedCode(dialog).value()).toContain('data-lang="de"');
  });

  it("offers the embed code as an iframe too, and copies it", async () => {
    const { dialog, copied } = await published();
    const { code } = embedCode(dialog);

    fireEvent.click(code.getByRole("tab", { name: "iframe" }));
    fireEvent.click(code.getByRole("button", { name: "Copy embed code" }));

    expect(copied).toEqual([
      '<iframe src="https://surveys.acme.at/s/kz26b7mq?embed" title="Customer satisfaction 2026" style="width:100%;height:720px;border:0" loading="lazy"></iframe>',
    ]);
  });

  it("has one Download QR button under the code", async () => {
    const { dialog } = await published();
    const qr = within(dialog.getByRole("group", { name: "QR code" }));

    expect(qr.getAllByRole("button").map((button) => button.textContent)).toEqual(["Download QR"]);
  });
});

describe("Published (AWK-7, W12 B): invite", () => {
  it("opens the invitation from the published view", async () => {
    const { dialog } = await published();

    fireEvent.click(dialog.getByRole("button", { name: "Invite people…" }));

    expect(screen.getByRole("dialog", { name: "Invite participants" })).toBeTruthy();
  });
});
