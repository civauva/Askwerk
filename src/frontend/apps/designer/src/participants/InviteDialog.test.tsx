// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { InviteDialog } from "./InviteDialog";
import { FakeParticipantsClient } from "./testing";

afterEach(cleanup);

function shown(client = new FakeParticipantsClient()) {
  const onClose = vi.fn();
  const view = render(
    <InviteDialog
      client={client}
      surveyId="s1"
      locale="en"
      preview={() => Promise.resolve({ subject: "Acme GmbH: your opinion counts", text: "Hello Anna Berger,\n\nplease answer by 28 October 2026." })}
      today={() => new Date("2026-10-14T10:00:00Z")}
      checkDelayMs={0}
      onClose={onClose}
    />,
  );
  const dialog = () => within(screen.getByRole("dialog", { name: "Invite participants" }));

  return { client, onClose, dialog, ...view };
}

async function pasted(dialog: () => ReturnType<typeof within>, text: string) {
  fireEvent.change(dialog().getByRole("textbox", { name: "Addresses, one per line" }), { target: { value: text } });
  await act(async () => {});
}

describe("Invite participants (AWK-7, W12 C): two steps", () => {
  it("checks pasted addresses and names what will not be invited", async () => {
    const { dialog, container } = shown();

    await pasted(dialog, "anna@acme.at\njosef@acme.at\nanna@acme.at\nmaria@acme");

    expect(await dialog().findByText("2 new · 1 twice in the list · not an address: line 4 “maria@acme”")).toBeTruthy();
    expect(dialog().getByRole("button", { name: "Next" }).hasAttribute("disabled")).toBe(false);
    await expectNoAxeViolations(container);
  });

  it("reads a CSV or Excel file", async () => {
    const { dialog, client } = shown();

    fireEvent.change(dialog().getByLabelText("Choose a CSV or Excel file"), { target: { files: [new File(["email\nlena@acme.at"], "list.xlsx")] } });

    expect(await dialog().findByText("1 new · 1 already invited")).toBeTruthy();
    expect(client.calls).toContainEqual(["checkFile", "s1", "list.xlsx"]);
  });

  it("sends with the deadline in two weeks and one reminder, as the defaults", async () => {
    const { dialog, client } = shown();
    await pasted(dialog, "anna@acme.at\njosef@acme.at");
    fireEvent.click(await dialog().findByRole("button", { name: "Next" }));

    expect(await dialog().findByText("Acme GmbH: your opinion counts", undefined, { timeout: 5000 })).toBeTruthy();
    expect((dialog().getByLabelText("Answer by") as HTMLInputElement).value).toBe("2026-10-28");
    expect((dialog().getByRole("checkbox", { name: "Remind those who have not answered, 3 days before the deadline" }) as HTMLInputElement).checked).toBe(true);
    fireEvent.click(dialog().getByRole("button", { name: "Send 2 invitations" }));

    expect((await dialog().findByRole("status")).textContent).toBe("Sent to 2 people. Results → Participants shows who answered.");
    expect(client.calls.at(-1)).toEqual([
      "invite",
      "s1",
      { participants: [{ email: "anna@acme.at", name: null, language: null }, { email: "josef@acme.at", name: null, language: null }], deadline: "2026-10-28", reminder: true, send: true },
    ]);
  });

  it("sends later when a time is chosen", async () => {
    const { dialog, client } = shown();
    await pasted(dialog, "anna@acme.at");
    fireEvent.click(await dialog().findByRole("button", { name: "Next" }));

    fireEvent.click(dialog().getByRole("radio", { name: "Later" }));
    fireEvent.change(dialog().getByLabelText("Send on"), { target: { value: "2026-10-21T09:00" } });
    fireEvent.click(dialog().getByRole("button", { name: "Send 1 invitation" }));
    await dialog().findByRole("status");

    expect((client.calls.at(-1)![2] as { sendAt: string }).sendAt).toBe(new Date("2026-10-21T09:00").toISOString());
  });

  it("makes personal links while email is not set up", async () => {
    const client = new FakeParticipantsClient();
    client.emailReady = false;
    const { dialog } = shown(client);
    await pasted(dialog, "anna@acme.at\njosef@acme.at");
    fireEvent.click(await dialog().findByRole("button", { name: "Next" }));
    fireEvent.click(dialog().getByRole("button", { name: "Send 2 invitations" }));

    expect((await dialog().findByRole("alert")).textContent).toBe("Email is not set up yet, so nothing can be sent. Make personal links instead and share them another way.");
    fireEvent.click(dialog().getByRole("button", { name: "Make 2 personal links" }));

    expect((await dialog().findByRole("status")).textContent).toBe("2 personal links made. Download them under Results → Participants.");
    expect((client.calls.at(-1)![2] as { send: boolean }).send).toBe(false);
  });
});
