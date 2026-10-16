// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ApiError } from "@askwerk/app-client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AccountApp } from "./AccountApp";
import { ChooseOrganisationPage } from "./ChooseOrganisationPage";
import { SetupWizard } from "./SetupWizard";
import { anna, FakeAuthClient, problem, traceId } from "./testing";
import { TwoFactorSetupPage } from "./TwoFactorSetupPage";

afterEach(cleanup);

const next = () => fireEvent.click(screen.getByRole("button", { name: "Next" }));
const type = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

/** Picks an option of a shared combo box (AWK-19): open it by its label, then click the option. */
function choose(scope: { getByRole: typeof screen.getByRole }, label: string, option: string) {
  const button = scope.getByRole("button", { name: new RegExp(`^${label}( |$)`) });
  expect(button.getAttribute("aria-haspopup")).toBe("listbox");
  fireEvent.click(button);
  fireEvent.click(screen.getByRole("option", { name: option }));
}


function fillAccountAndInstance() {
  type("Name", "Anna Berger");
  type("Email", "anna@acme.at");
  type("Password", "correct horse battery");
  type("Repeat password", "correct horse battery");
  next();
  type("Instance name", "Acme Surveys");
  next();
}

describe("SetupWizard (M2)", () => {
  it("takes the default language from the shared combo box", async () => {
    const client = new FakeAuthClient();
    render(<SetupWizard client={client} locale="en" baseAddress="https://surveys.acme.at" timeZone="Europe/Vienna" onDone={vi.fn()} />);
    type("Name", "Anna Berger");
    type("Email", "anna@acme.at");
    type("Password", "correct horse battery");
    type("Repeat password", "correct horse battery");
    next();

    choose(screen, "Default language", "German");
    type("Instance name", "Acme Surveys");
    next();
    type("Organisation name", "Acme GmbH");
    type("Short name", "acme");
    next();
    fireEvent.click(await screen.findByRole("button", { name: "Skip for now" }));

    await waitFor(() => expect(client.calls.find(([name]) => name === "completeSetup")?.[1]).toMatchObject({ instance: { defaultLanguage: "de" } }));
  });

  it("walks through account, instance, organisation and email, then sends everything at once", async () => {
    const client = new FakeAuthClient();
    const onDone = vi.fn();
    render(<SetupWizard client={client} locale="en" baseAddress="https://surveys.acme.at" timeZone="Europe/Vienna" onDone={onDone} />);

    fillAccountAndInstance();
    type("Organisation name", "Acme GmbH");
    type("Short name", "acme");
    next();
    fireEvent.click(await screen.findByRole("button", { name: "Skip for now" }));

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("two-factor-setup-required"));
    expect(client.calls).toContainEqual([
      "completeSetup",
      {
        owner: { name: "Anna Berger", email: "anna@acme.at", password: "correct horse battery" },
        instance: { name: "Acme Surveys", baseAddress: "https://surveys.acme.at", defaultLanguage: "en", timeZone: "Europe/Vienna" },
        organisation: { name: "Acme GmbH", slug: "acme" },
      },
    ]);
  });

  it("shows which step is current and which are done", () => {
    render(<SetupWizard client={new FakeAuthClient()} locale="en" baseAddress="https://surveys.acme.at" timeZone="Europe/Vienna" onDone={vi.fn()} />);

    fillAccountAndInstance();

    const steps = within(screen.getByRole("list", { name: "Setup steps" })).getAllByRole("listitem");
    expect(steps.map((step) => step.getAttribute("aria-current"))).toEqual([null, null, "step", null]);
    expect(screen.getByRole("heading", { name: "First organisation" })).toBeTruthy();
  });

  it("keeps the owner on step 1 until the passwords match and are long enough", () => {
    render(<SetupWizard client={new FakeAuthClient()} locale="en" baseAddress="https://surveys.acme.at" timeZone="Europe/Vienna" onDone={vi.fn()} />);
    type("Name", "Anna Berger");
    type("Email", "anna@acme.at");
    type("Password", "short");
    type("Repeat password", "shorter");

    next();

    expect(screen.getByText("At least 12 characters.").className).toContain("error");
    expect(screen.getByText("The passwords are not the same.")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Your account" })).toBeTruthy();
  });

  it("checks the short name and offers the suggestion when it is taken", async () => {
    const client = new FakeAuthClient();
    client.slugAnswer = { available: false, suggestion: "acme-gmbh" };
    render(<SetupWizard client={client} locale="en" baseAddress="https://surveys.acme.at" timeZone="Europe/Vienna" onDone={vi.fn()} />);
    fillAccountAndInstance();
    type("Organisation name", "Acme GmbH");
    type("Short name", "acme");

    next();

    expect(await screen.findByText("“acme” is already taken.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Use acme-gmbh" }));
    expect((screen.getByLabelText("Short name") as HTMLInputElement).value).toBe("acme-gmbh");
  });

  it("sends a test email with the entered server and includes the settings", async () => {
    const client = new FakeAuthClient();
    render(<SetupWizard client={client} locale="en" baseAddress="https://surveys.acme.at" timeZone="Europe/Vienna" onDone={vi.fn()} />);
    fillAccountAndInstance();
    type("Organisation name", "Acme GmbH");
    type("Short name", "acme");
    next();
    await screen.findByRole("heading", { name: "Email" });
    type("SMTP server", "smtp.acme.at");
    type("User", "surveys@acme.at");
    type("SMTP password", "smtp-secret");
    type("Sender", "Acme Surveys <surveys@acme.at>");

    fireEvent.click(screen.getByRole("button", { name: "Send test email" }));
    expect(await screen.findByText("Sent to anna@acme.at")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Finish setup" }));

    await waitFor(() => expect(client.calls.find((call) => call[0] === "completeSetup")?.[1]).toMatchObject({
      email: { host: "smtp.acme.at", port: 587, userName: "surveys@acme.at", password: "smtp-secret", sender: "Acme Surveys <surveys@acme.at>" },
    }));
    expect(client.calls).toContainEqual(["sendTestEmail", { host: "smtp.acme.at", port: 587, userName: "surveys@acme.at", password: "smtp-secret", sender: "Acme Surveys <surveys@acme.at>" }, "anna@acme.at", "en"]);
  });

  it("has no accessibility violations", async () => {
    const { container } = render(<SetupWizard client={new FakeAuthClient()} locale="de" baseAddress="https://surveys.acme.at" timeZone="Europe/Vienna" onDone={vi.fn()} />);
    await act(() => Promise.resolve());

    await expectNoAxeViolations(container);
  });
});

describe("TwoFactorSetupPage (M3)", () => {
  it("shows the QR code and the key, turns two-factor on with a code, then shows the recovery codes once", async () => {
    const client = new FakeAuthClient();
    const onDone = vi.fn();
    render(<TwoFactorSetupPage client={client} locale="en" onDone={onDone} />);

    expect(await screen.findByRole("img", { name: "QR code for your authenticator app" })).toBeTruthy();
    expect(screen.getByText("JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP")).toBeTruthy();
    fireEvent.change(screen.getByRole("textbox", { name: "Digit 1 of 6" }), { target: { value: "913502" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm and continue" }));

    expect(await screen.findByRole("heading", { name: "Keep your recovery codes" })).toBeTruthy();
    expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual(["AAAAA-11111", "BBBBB-22222"]);
    fireEvent.click(screen.getByRole("button", { name: "I have saved them" }));
    expect(onDone).toHaveBeenCalled();
    expect(client.calls).toContainEqual(["enableTwoFactor", "913502"]);
  });

  it("says when the code is wrong", async () => {
    const client = new FakeAuthClient();
    client.enableAnswers = [problem(400, { errors: { code: ["code-invalid"] } })];
    render(<TwoFactorSetupPage client={client} locale="de" onDone={vi.fn()} />);
    await screen.findByRole("img", { name: "QR-Code für Ihre Authenticator-App" });

    fireEvent.change(screen.getByRole("textbox", { name: "Ziffer 1 von 6" }), { target: { value: "000000" } });
    fireEvent.click(screen.getByRole("button", { name: "Bestätigen und weiter" }));

    expect(await screen.findByText("Dieser Code ist nicht gültig. Codes ändern sich alle 30 Sekunden.")).toBeTruthy();
  });
});

describe("ChooseOrganisationPage (M3)", () => {
  it("lists the organisations with role and switches to the chosen one", async () => {
    const client = new FakeAuthClient();
    const onDone = vi.fn();
    const organisations = [
      { id: "a", name: "Acme GmbH", slug: "acme", role: "owner" },
      { id: "b", name: "Verein Lesefreunde", slug: "lesefreunde", role: "analyst" },
    ];
    render(<ChooseOrganisationPage client={client} locale="de" me={{ ...anna, organisations, currentOrganisationId: null }} onDone={onDone} />);

    fireEvent.click(screen.getByRole("button", { name: "Verein Lesefreunde Analyst:in" }));

    await waitFor(() => expect(onDone).toHaveBeenCalled());
    expect(client.calls).toContainEqual(["switchOrganisation", "b"]);
  });
});

describe("AccountApp (routing)", () => {
  it("opens Account and security at /account, in the language saved with the account", async () => {
    const client = new FakeAuthClient();
    client.account = { ...anna, language: "de" };
    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/account", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByRole("heading", { level: 1, name: "Konto und Sicherheit" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Konto: Anna Berger" })).toBeTruthy();
  });

  it("gives the home its path and lets it move between the list and a survey", async () => {
    const navigate = vi.fn();
    render(
      <AccountApp
        client={new FakeAuthClient()}
        initialLocale="en"
        location={{ pathname: "/surveys/s1", search: "" }}
        navigate={navigate}
        home={({ path, onNavigate }) => (
          <button type="button" onClick={() => onNavigate("/")}>
            {`At ${path}`}
          </button>
        )}
      />,
    );

    fireEvent.click(await screen.findByRole("button", { name: "At /surveys/s1" }));

    expect(await screen.findByRole("button", { name: "At /" })).toBeTruthy();
    expect(navigate).toHaveBeenCalledWith("/");
  });

  it("goes from the account menu to Account and security", async () => {
    const navigate = vi.fn();
    render(
      <AccountApp
        client={new FakeAuthClient()}
        initialLocale="en"
        location={{ pathname: "/", search: "" }}
        navigate={navigate}
        home={({ onOpenAccount }) => (
          <button type="button" onClick={onOpenAccount}>
            Open account
          </button>
        )}
      />,
    );

    fireEvent.click(await screen.findByRole("button", { name: "Open account" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Account and security" })).toBeTruthy();
    expect(navigate).toHaveBeenCalledWith("/account");
  });

  it("opens the setup wizard on an empty installation", async () => {
    const client = new FakeAuthClient();
    client.setupNeeded = true;

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByRole("heading", { name: "Your account" })).toBeTruthy();
  });

  it("asks a signed-out user to sign in", async () => {
    const client = new FakeAuthClient();
    client.account = null;

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeTruthy();
  });

  it("sends an owner without two-factor to the two-factor setup", async () => {
    const client = new FakeAuthClient();
    client.account = { ...anna, state: "two-factor-setup-required", twoFactorEnabled: false };

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByRole("heading", { name: "Set up two-factor" })).toBeTruthy();
  });

  it("explains when the account belongs to no organisation", async () => {
    const client = new FakeAuthClient();
    client.account = { ...anna, organisations: [], currentOrganisationId: null };

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByRole("heading", { name: "No access yet" })).toBeTruthy();
  });

  it("shows the app once everything is in place", async () => {
    render(<AccountApp client={new FakeAuthClient()} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByText("Home")).toBeTruthy();
  });

  it("opens the new-password form from the link in the reset email", async () => {
    const client = new FakeAuthClient();
    client.account = null;

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/reset-password", search: "?email=anna%40acme.at&token=t0ken" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByLabelText("New password")).toBeTruthy();
  });

  it("says when Askwerk does not answer, with the reference, and tries again", async () => {
    const client = new FakeAuthClient();
    let calls = 0;
    client.setupRequired = () => {
      calls += 1;
      return calls === 1 ? Promise.reject(new ApiError(0, traceId, "network")) : Promise.resolve(false);
    };

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    expect(await screen.findByRole("heading", { name: "Something went wrong" })).toBeTruthy();
    expect(screen.getByText(`Reference: ${traceId}`)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Home")).toBeTruthy();
  });

  it("waits while Askwerk is still starting and tries again every 5 seconds", async () => {
    vi.useFakeTimers();
    const client = new FakeAuthClient();
    let calls = 0;
    client.setupRequired = () => {
      calls += 1;
      return calls === 1 ? Promise.reject(new ApiError(503, traceId, "starting")) : Promise.resolve(false);
    };
    render(<AccountApp client={client} initialLocale="de" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);
    await act(() => vi.advanceTimersByTimeAsync(0));
    const starting = screen.getByRole("heading", { name: "Askwerk startet" });

    await act(() => vi.advanceTimersByTimeAsync(5000));

    expect(starting).toBeTruthy();
    expect(screen.getByText("Home")).toBeTruthy();
    vi.useRealTimers();
  });

  it("shows the installation's name above the sign-in form", async () => {
    const client = new FakeAuthClient();
    client.account = null;

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} />);

    await screen.findByRole("heading", { name: "Sign in" });
    expect(screen.getByText("Acme Surveys")).toBeTruthy();
  });

  it("opens the organisation chosen with “Open directly next time” without asking again", async () => {
    const remembered = new Map<string, string>();
    const client = new FakeAuthClient();
    const organisations = [
      { id: "a", name: "Acme GmbH", slug: "acme", role: "owner" },
      { id: "b", name: "Verein Lesefreunde", slug: "lesefreunde", role: "analyst" },
    ];
    client.account = { ...anna, organisations, currentOrganisationId: null };
    const storage = { get: (key: string) => remembered.get(key) ?? null, set: (key: string, value: string) => void remembered.set(key, value) };
    const view = render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} storage={storage} />);
    fireEvent.click(await screen.findByRole("checkbox", { name: "Open directly next time" }));
    client.account = { ...anna, organisations, currentOrganisationId: "b" };
    fireEvent.click(screen.getByRole("button", { name: "Verein Lesefreunde Analyst" }));
    await screen.findByText("Home");
    view.unmount();
    client.account = { ...anna, organisations, currentOrganisationId: null };
    client.calls.length = 0;

    render(<AccountApp client={client} initialLocale="en" location={{ pathname: "/", search: "" }} navigate={vi.fn()} home={() => <p>Home</p>} storage={storage} />);

    await waitFor(() => expect(client.calls).toContainEqual(["switchOrganisation", "b"]));
  });
});

describe("Joining from an invitation link (AWK-30, W18 C)", () => {
  it("signs in with the existing account first, then joins and opens the app", async () => {
    const { FakeMembersClient, sampleInvitation } = await import("../members/testing");
    const members = new FakeMembersClient();
    members.joinable = { ...sampleInvitation, accountExists: true };
    const client = new FakeAuthClient();
    client.account = null;
    const navigate = vi.fn();
    const Home = () => <p>Home</p>;
    render(<AccountApp client={client} members={members} initialLocale="en" location={{ pathname: "/join/abc", search: "" }} navigate={navigate} home={Home} />);

    fireEvent.click(await screen.findByRole("button", { name: "Sign in to join" }));
    fireEvent.change(await screen.findByRole("textbox", { name: "Email" }), { target: { value: "ben@acme.at" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "correct horse battery" } });
    client.account = anna;
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    await waitFor(() => expect(members.calls).toContainEqual(["join", "abc", undefined]));
    expect(await screen.findByText("Home")).toBeTruthy();
    expect(navigate).toHaveBeenLastCalledWith("/");
  });
});
