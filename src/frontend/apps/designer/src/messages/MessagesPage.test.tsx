// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { MessagesPage } from "./MessagesPage";
import { emailNotSetUp, FakeMessagesClient } from "./testing";

afterEach(cleanup);

const surveys = [
  { id: "s1", title: "Customer satisfaction 2026" },
  { id: "s2", title: "Employee pulse Q4" },
];

async function shown(client = new FakeMessagesClient(), locale: "de" | "en" = "en") {
  const view = render(<MessagesPage client={client} surveys={surveys} locale={locale} saveDelayMs={0} />);
  await screen.findByRole("heading", { level: 1 });
  await act(async () => {});

  return { client, ...view };
}

const editor = () => within(screen.getByRole("region", { name: "Message" }));

describe("Messages (AWK-17 step 6, W10 D): stupid simple", () => {
  it("shows invitation, reminder and thanks, with Askwerk's text in the editing language", async () => {
    await shown();

    const kinds = within(screen.getByRole("navigation", { name: "Messages" }));
    expect(kinds.getAllByRole("button").map((button) => button.getAttribute("aria-label"))).toEqual(["Invitation", "Reminder", "Thank you"]);
    expect((editor().getByRole("textbox", { name: "Subject" }) as HTMLInputElement).value).toBe("{organisation}: your opinion counts");
    expect(editor().getByText("Askwerk's text. Change it to use your own.")).toBeTruthy();
    fireEvent.click(editor().getByRole("button", { name: /: (English|Deutsch)$/ }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: /Deutsch/ }));
    expect((editor().getByRole("textbox", { name: "Subject" }) as HTMLInputElement).value).toBe("{organisation}: Ihre Meinung zählt");
  });

  it("saves by itself, and not while a placeholder is unknown", async () => {
    const { client } = await shown();

    fireEvent.change(editor().getByRole("textbox", { name: "Text" }), { target: { value: "Bitte bis {frist}: {link}" } });
    expect(editor().getByRole("alert").textContent).toBe("Unknown placeholder {frist}. Use {name}, {link}, {deadline}, {duration}, {organisation} or {survey}.");
    expect(client.calls.some(([name]) => name === "save")).toBe(false);
    fireEvent.change(editor().getByRole("textbox", { name: "Text" }), { target: { value: "Please by {deadline}: {link}" } });

    await waitFor(() =>
      expect(client.calls).toContainEqual([
        "save",
        "invitation",
        { subject: { de: "{organisation}: Ihre Meinung zählt", en: "{organisation}: your opinion counts" }, body: { de: "Guten Tag {name},\n\n{link}", en: "Please by {deadline}: {link}" }, answerInEmail: true },
      ]),
    );
  });

  it("adds a placeholder to the text with one click", async () => {
    await shown();

    fireEvent.click(editor().getByRole("button", { name: "{deadline}" }));

    expect((editor().getByRole("textbox", { name: "Text" }) as HTMLTextAreaElement).value).toBe("Hello {name},\n\n{link}{deadline}");
  });

  it("previews the email for a survey, as respondents get it", async () => {
    const { client } = await shown();

    const preview = within(screen.getByRole("region", { name: "Preview" }));
    const frame = (await preview.findByTitle("Email preview")) as HTMLIFrameElement;
    expect(frame.getAttribute("srcdoc")).toContain("Preview of s1");
    expect(frame.getAttribute("sandbox")).toBe("");
    expect(preview.getByText("Acme GmbH: your opinion counts")).toBeTruthy();
    // On its stage (W32): a phone's width, and the zoom.
    fireEvent.click(preview.getByRole("button", { name: "Phone" }));
    expect(frame.parentElement!.style.width).toBe("390px");
    fireEvent.click(preview.getByRole("button", { name: "Zoom out" }));
    expect(frame.parentElement!.style.transform).toBe("scale(0.9)");
    fireEvent.click(preview.getByRole("button", { name: /^Preview with/ }));
    fireEvent.click(screen.getByRole("option", { name: "Employee pulse Q4" }));

    await waitFor(() => expect(client.calls.at(-1)).toEqual(["preview", "invitation", expect.objectContaining({ surveyId: "s2", language: "en" })]));
  });

  it("sends a test to the signed-in user, and says when email is not set up", async () => {
    const client = new FakeMessagesClient();
    await shown(client);
    const preview = within(screen.getByRole("region", { name: "Preview" }));

    fireEvent.click(preview.getByRole("button", { name: "Send test to me" }));
    expect(await preview.findByText("Sent to your address. Check your inbox.")).toBeTruthy();
    client.testAnswer = emailNotSetUp();
    fireEvent.click(preview.getByRole("button", { name: "Send test to me" }));

    expect(await preview.findByText("Email is not set up yet. An owner sets it up in the instance settings.")).toBeTruthy();
  });

  it("speaks German", async () => {
    await shown(new FakeMessagesClient(), "de");

    expect(screen.getByRole("heading", { level: 1, name: "Nachrichten" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Test an mich senden" })).toBeTruthy();
  });

  it("has no accessibility violations", async () => {
    const { container } = await shown();
    await screen.findByTitle("Email preview");

    await expectNoAxeViolations(container);
  });
});
