// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { JoinPage } from "./JoinPage";
import { FakeMembersClient, sampleInvitation } from "./testing";

afterEach(cleanup);

function shown(members = new FakeMembersClient(), locale: "de" | "en" = "en") {
  const onJoined = vi.fn();
  const view = render(<JoinPage members={members} token="abc" locale={locale} instanceName="Acme Surveys" onJoined={onJoined} onSignIn={() => undefined} />);

  return { members, onJoined, ...view };
}

describe("Joining from the link (AWK-30, W18 C)", () => {
  it("lets a new person set name and password, and joins", async () => {
    const { members, onJoined, container } = shown();

    expect(await screen.findByRole("heading", { level: 1, name: "Join Acme GmbH as Editor" })).toBeTruthy();
    expect(screen.getByText("Invited by Anna Berger")).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "Email" }) as HTMLInputElement).readOnly).toBe(true);
    fireEvent.change(screen.getByRole("textbox", { name: "Name" }), { target: { value: "Ben Huber" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "a long enough password" } });
    fireEvent.click(screen.getByRole("button", { name: "Join Acme GmbH" }));

    await waitFor(() => expect(onJoined).toHaveBeenCalledWith("signed-in"));
    expect(members.calls).toContainEqual(["join", "abc", { name: "Ben Huber", password: "a long enough password" }]);
    await expectNoAxeViolations(container);
  });

  it("asks for at least 12 characters before trying", async () => {
    const { members } = shown();

    await screen.findByRole("heading", { level: 1 });
    fireEvent.change(screen.getByRole("textbox", { name: "Name" }), { target: { value: "Ben Huber" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "short" } });
    fireEvent.click(screen.getByRole("button", { name: "Join Acme GmbH" }));

    expect(await screen.findByText("Use at least 12 characters.")).toBeTruthy();
    expect(members.calls.some(([name]) => name === "join")).toBe(false);
  });

  it("joins with the account signed in in this browser", async () => {
    const members = new FakeMembersClient();
    members.joinable = { ...sampleInvitation, role: "viewer", accountExists: true, signedInAs: "ben@acme.at" };
    const { onJoined } = shown(members, "de");

    expect(await screen.findByRole("heading", { level: 1, name: "Acme GmbH als Lesen (Viewer) beitreten" })).toBeTruthy();
    expect(screen.getByText("Angemeldet als ben@acme.at. Ihre anderen Organisationen bleiben; Sie wechseln im Organisationsmenü.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Acme GmbH beitreten" }));

    await waitFor(() => expect(onJoined).toHaveBeenCalledWith("signed-in"));
    expect(members.calls).toContainEqual(["join", "abc", undefined]);
  });

  it("sends someone with an account to sign in first", async () => {
    const members = new FakeMembersClient();
    members.joinable = { ...sampleInvitation, accountExists: true };
    const onSignIn = vi.fn();
    render(<JoinPage members={members} token="abc" locale="en" instanceName="Acme Surveys" onJoined={() => undefined} onSignIn={onSignIn} />);

    expect(await screen.findByText("ben@acme.at already has an account. Sign in, and Acme GmbH is added to it.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Sign in to join" }));

    expect(onSignIn).toHaveBeenCalled();
  });

  it("says when the link no longer works", async () => {
    const members = new FakeMembersClient();
    members.joinable = "gone";
    shown(members);

    expect(await screen.findByRole("heading", { level: 1, name: "This invitation no longer works" })).toBeTruthy();
    expect(screen.getByText("A link works once, for 7 days. Ask whoever invited you for a new one.")).toBeTruthy();
  });
});
