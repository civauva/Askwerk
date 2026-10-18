// SPDX-License-Identifier: AGPL-3.0-only
import type { Me } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { createPortal } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { anna, FakeAuthClient } from "../account/testing";
import { AppShell, placeOf, useShellSlot, useShellTemplate, type Place } from "./AppShell";

afterEach(cleanup);

const hr = { id: "0199e000-0000-7000-8000-00000000000b", name: "Acme HR Services", slug: "acme-hr", role: "admin", status: "active" };
const lab = { id: "0199e000-0000-7000-8000-00000000000c", name: "Test lab", slug: "test", role: "editor", status: "suspended" };

function shown({ me = anna, active = "surveys" as Place | null, survey }: { me?: Me; active?: Place | null; survey?: { onBack: () => void } } = {}, child = <main>Page</main>) {
  const client = new FakeAuthClient();
  const handlers = { onNavigate: vi.fn(), onSignedOut: vi.fn(), onSwitched: vi.fn(), onOpenAccount: vi.fn() };
  const view = render(
    <AppShell client={client} locale="en" me={me} active={active} {...handlers} {...(survey !== undefined && { survey })}>
      {child}
    </AppShell>,
  );
  const side = within(screen.getByRole("complementary", { name: "Navigation" }));

  return { client, side, ...handlers, ...view };
}

describe("AppShell: the sidebar (AWK-38, W29)", () => {
  it("shows the places and one Administration for an owner, marks the current place and goes to another", async () => {
    const { side, onNavigate, container } = shown();

    expect(side.getByRole("link", { name: "Surveys" }).getAttribute("aria-current")).toBe("page");
    expect(side.getAllByRole("link").map((link) => link.textContent)).toEqual([
      "askwerk",
      "Surveys",
      "Templates",
      "Organisation",
      "Members",
      "Contacts",
      "Connections",
      "Sign-in",
      "Looks",
      "Messages",
      "Domains",
      "Developers & API",
    ]);
    expect(side.queryByText("This Askwerk")).toBeNull();
    fireEvent.click(side.getByRole("link", { name: "Members" }));
    expect(onNavigate).toHaveBeenCalledWith("/members");
    await expectNoAxeViolations(container);
  });

  it("shows a count beside a place once it is known (W35: Contacts 1,284)", () => {
    render(
      <AppShell client={new FakeAuthClient()} locale="en" me={anna} active="contacts" onNavigate={vi.fn()} onSignedOut={vi.fn()} onSwitched={vi.fn()} onOpenAccount={vi.fn()} counts={{ contacts: 1284 }}>
        <main>Page</main>
      </AppShell>,
    );

    expect(within(screen.getByRole("complementary", { name: "Navigation" })).getByRole("link", { name: /^Contacts/ }).textContent).toBe("Contacts1,284");
  });

  it("shows an editor only Developers & API in Administration, and installation administrators This Askwerk", () => {
    const { side } = shown({ me: { ...anna, organisations: [{ ...anna.organisations[0]!, role: "editor" }] } });

    expect(side.queryByRole("link", { name: "Members" })).toBeNull();
    expect(side.getByRole("link", { name: "Developers & API" })).toBeTruthy();
    cleanup();

    const admin = shown({ me: { ...anna, installationAdmin: true } }).side;
    expect(admin.getByText("This Askwerk")).toBeTruthy();
    expect(admin.getByRole("link", { name: "Organisations" }).getAttribute("href")).toBe("/installation/organisations");
    expect(admin.getByRole("link", { name: "Administrators" })).toBeTruthy();
    expect(admin.getByRole("link", { name: "Installation" })).toBeTruthy();
  });

  it("switches organisation from the box, lands on its survey list, and cannot pick a suspended one", async () => {
    const { side, client, onSwitched } = shown({ me: { ...anna, installationAdmin: true, organisations: [...anna.organisations, hr, lab] } });

    fireEvent.click(side.getByRole("button", { name: "Organisation: Acme GmbH" }));
    const menu = within(screen.getByRole("menu"));
    expect(menu.getByRole("menuitem", { name: /Test lab/ }).hasAttribute("disabled")).toBe(true);
    expect(menu.getByRole("menuitem", { name: "New organisation" })).toBeTruthy();
    fireEvent.click(menu.getByRole("menuitem", { name: /Acme HR Services/ }));

    await waitFor(() => expect(onSwitched).toHaveBeenCalled());
    expect(client.calls).toContainEqual(["switchOrganisation", hr.id]);
  });

  it("steps into a survey: All surveys goes back, and the survey's screen fills the sidebar", () => {
    const onBack = vi.fn();

    function SurveyScreen() {
      const slot = useShellSlot();
      return slot === null ? null : createPortal(<nav aria-label="Survey">Questions</nav>, slot);
    }

    const { side } = shown({ survey: { onBack } }, <SurveyScreen />);

    expect(side.queryByRole("link", { name: "Members" })).toBeNull();
    expect(side.getByRole("navigation", { name: "Survey" })).toBeTruthy();
    fireEvent.click(side.getByRole("link", { name: "All surveys" }));
    expect(onBack).toHaveBeenCalled();
  });

  it("inside a template, the way back says All templates and leads to Templates (W32)", () => {
    const onBack = vi.fn();

    function TemplateScreen() {
      useShellTemplate(true);
      return null;
    }

    const { side, onNavigate } = shown({ survey: { onBack } }, <TemplateScreen />);

    fireEvent.click(side.getByRole("link", { name: "All templates" }));
    expect(onNavigate).toHaveBeenCalledWith("/templates");
    expect(onBack).not.toHaveBeenCalled();
  });

  it("finds the place of a path", () => {
    expect([placeOf("/"), placeOf("/surveys/s1/results"), placeOf("/api-keys"), placeOf("/installation"), placeOf("/account")]).toEqual(["surveys", "surveys", "developers", "installation", null]);
  });
});
