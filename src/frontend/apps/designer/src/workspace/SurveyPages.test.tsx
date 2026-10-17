// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { FakeParticipantsClient } from "../participants/testing";
import { ParticipantsPage, SharePage } from "./index";
import { FakeWorkspaceSurveys, publishedSummary } from "./testing";

afterEach(cleanup);

const tabs = () => within(screen.getByRole("navigation", { name: "Survey" }));
const bar = () => within(screen.getByRole("banner"));

async function share(surveys = new FakeWorkspaceSurveys()) {
  const onTab = vi.fn();
  const view = render(
    <SharePage
      surveys={surveys}
      participants={new FakeParticipantsClient()}
      surveyId="s1"
      locale="en"
      canEdit
      onTab={onTab}
      onBack={() => undefined}
      writeClipboard={() => Promise.resolve()}
      saveFile={() => undefined}
    />,
  );
  await screen.findByRole("heading", { level: 1 });
  await act(async () => {});

  return { onTab, ...view };
}

describe("Share tab (AWK-29, W17 C)", () => {
  it("gives the link, QR code, embed code and invitations of a published survey", async () => {
    const { container } = await share();

    expect(tabs().getByRole("link", { name: "Share" }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByText("https://surveys.acme.at/s/kz26b7mq")).toBeTruthy();
    expect(screen.getByRole("img", { name: /QR code/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Get embed code" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Invite people…" })).toBeTruthy();
    expect(await screen.findByText(/^Personal links; each person answers once\. \d+ invited, 1 answered\.$/)).toBeTruthy();
    expect(bar().queryByRole("button", { name: "Share" })).toBeNull();
    // The bar names the place; no second "Share" heading (W34).
    expect(screen.queryByRole("heading", { level: 2, name: "Share" })).toBeNull();
    await expectNoAxeViolations(container);
  });

  it("says a draft is shared once published, and leads to Build", async () => {
    const { onTab } = await share(new FakeWorkspaceSurveys({ ...publishedSummary, status: "draft", publishedVersion: null }));

    expect(screen.getByText("Publish the survey first; its link, QR code and embed code show here then.")).toBeTruthy();
    fireEvent.click(bar().getByRole("button", { name: "Publish" }));

    expect(onTab).toHaveBeenCalledWith("build");
  });
});

describe("Participants tab (AWK-29, W17 E)", () => {
  it("lists who was invited, as its own tab", async () => {
    const onTab = vi.fn();
    const { container } = render(
      <ParticipantsPage surveys={new FakeWorkspaceSurveys()} participants={new FakeParticipantsClient()} surveyId="s1" locale="en" canEdit onTab={onTab} onBack={() => undefined} timeZone="UTC" />,
    );

    const grid = await screen.findByRole("grid", { name: "Participants" });
    await within(grid).findByText("anna@acme.at · CRM-1");
    expect(tabs().getByRole("link", { name: "Participants 640" }).getAttribute("aria-current")).toBe("page");
    fireEvent.click(tabs().getByRole("link", { name: "Results 248" }));
    expect(onTab).toHaveBeenCalledWith("results");
    await expectNoAxeViolations(container);
  });

  it("shows readers only Collect → Participants and Analyse → Results (W23)", async () => {
    render(<ParticipantsPage surveys={new FakeWorkspaceSurveys()} participants={new FakeParticipantsClient()} surveyId="s1" locale="de" canEdit={false} onTab={() => undefined} onBack={() => undefined} timeZone="UTC" />);

    await screen.findByRole("grid");
    expect(within(screen.getByRole("navigation", { name: "Umfrage" })).getAllByRole("link").map((tab) => tab.textContent)).toEqual(["Teilnehmende640", "Ergebnisse248", "Einstellungen"]);
  });
});
