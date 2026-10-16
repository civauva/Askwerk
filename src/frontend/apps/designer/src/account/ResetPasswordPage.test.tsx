// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ResetPasswordPage } from "./ResetPasswordPage";
import { FakeAuthClient, problem } from "./testing";

afterEach(cleanup);

describe("ResetPasswordPage (M1)", () => {
  it("sends the link and says so without revealing whether the account exists", async () => {
    const client = new FakeAuthClient();
    render(<ResetPasswordPage client={client} locale="en" onBack={vi.fn()} />);

    fireEvent.change(await screen.findByRole("textbox", { name: "Email" }), { target: { value: "anna@acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Send link" }));

    expect((await screen.findByRole("status")).textContent).toBe("If an account exists for this address, the link is on its way.Check your spam folder, too.");
    expect(client.calls).toContainEqual(["requestPasswordReset", "anna@acme.at"]);
  });

  it("explains when reset by email is not set up", async () => {
    const client = new FakeAuthClient();
    client.resetAvailable = false;
    render(<ResetPasswordPage client={client} locale="de" onBack={vi.fn()} />);

    expect(await screen.findByText("Zurücksetzen per E-Mail ist nicht eingerichtet. Bitte wenden Sie sich an Ihre Administration.")).toBeTruthy();
  });

  it("sets the new password from the link", async () => {
    const client = new FakeAuthClient();
    const onBack = vi.fn();
    render(<ResetPasswordPage client={client} locale="en" link={{ email: "anna@acme.at", token: "t0ken" }} onBack={onBack} />);

    fireEvent.change(screen.getByLabelText("New password"), { target: { value: "a brand new passphrase" } });
    fireEvent.click(screen.getByRole("button", { name: "Set password" }));

    expect(await screen.findByText("Password changed. Sign in with the new one.")).toBeTruthy();
    expect(client.calls).toContainEqual(["confirmPasswordReset", "anna@acme.at", "t0ken", "a brand new passphrase"]);
  });

  it("says when the link has expired or was used", async () => {
    const client = new FakeAuthClient();
    client.resetConfirmAnswers = [problem(400, { errors: { token: ["token-invalid"] } })];
    render(<ResetPasswordPage client={client} locale="en" link={{ email: "anna@acme.at", token: "old" }} onBack={vi.fn()} />);

    fireEvent.change(screen.getByLabelText("New password"), { target: { value: "a brand new passphrase" } });
    fireEvent.click(screen.getByRole("button", { name: "Set password" }));

    expect(await screen.findByText("This link has expired or was used already. Ask for a new one.")).toBeTruthy();
  });

  it("explains a too short password at the field", async () => {
    const client = new FakeAuthClient();
    client.resetConfirmAnswers = [problem(400, { errors: { password: ["password-too-short"] } })];
    render(<ResetPasswordPage client={client} locale="en" link={{ email: "anna@acme.at", token: "t" }} onBack={vi.fn()} />);

    fireEvent.change(screen.getByLabelText("New password"), { target: { value: "short" } });
    fireEvent.click(screen.getByRole("button", { name: "Set password" }));

    expect(await screen.findByText("At least 12 characters.")).toBeTruthy();
    expect(screen.getByLabelText("New password").getAttribute("aria-invalid")).toBe("true");
  });

  it("has no accessibility violations", async () => {
    const { container } = render(<ResetPasswordPage client={new FakeAuthClient()} locale="en" onBack={vi.fn()} />);
    await screen.findByRole("textbox", { name: "Email" });

    await expectNoAxeViolations(container);
  });
});
