// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ResultsPage } from "./ResultsPage";
import { FakeParticipantsClient } from "../participants/testing";
import { ParticipantsPage } from "../workspace";
import { FakeWorkspaceSurveys } from "../workspace/testing";
import { ApiError } from "@askwerk/app-client";
import { exportItem, exportNotAllowed, FakeResultsClient, readyExport, results } from "./testing";

afterEach(cleanup);
URL.createObjectURL = () => "blob:askwerk-test";
URL.revokeObjectURL = () => undefined;

async function shown(client = new FakeResultsClient(), locale: "de" | "en" = "en", canEdit = true) {
  const onTab = vi.fn();
  const onBack = vi.fn();
  const view = render(
    <ResultsPage
      client={client}
      surveys={new FakeWorkspaceSurveys()}
      surveyId="s1"
      surveyLink="https://surveys.acme.at/s/s1"
      locale={locale}
      onTab={onTab}
      onBack={onBack}
      searchDelayMs={0}
      pollMs={0}
      canEdit={canEdit}
      timeZone="UTC"
    />,
  );
  await screen.findByRole("heading", { level: 1 });
  await act(async () => {});

  return { client, onTab, onBack, ...view };
}

/** The Participants tab (W17 E), which was a tab of the results before AWK-29. */
async function participantsShown(participants = new FakeParticipantsClient(), saveFile?: (name: string, file: Blob) => void) {
  const view = render(
    <ParticipantsPage
      surveys={new FakeWorkspaceSurveys()}
      participants={participants}
      {...(saveFile === undefined ? {} : { saveFile })}
      preview={() => Promise.resolve({ subject: "Invitation", text: "Hello" })}
      surveyId="s1"
      locale="en"
      canEdit
      onTab={() => undefined}
      onBack={() => undefined}
      timeZone="UTC"
    />,
  );
  await screen.findByRole("grid", { name: "Participants" });

  return view;
}

const card = (name: string) => within(screen.getByRole("region", { name }));

async function choose(label: string, option: string) {
  fireEvent.click(screen.getByRole("button", { name: new RegExp(`^${label} `) }));
  fireEvent.click(await screen.findByRole("option", { name: option }));
}

describe("Results (AWK-24, W11): overview per question", () => {
  it("shows the totals and a summary card per question, in the survey's order", async () => {
    const { container } = await shown();

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Customer satisfaction 2026");
    expect(screen.getByRole("heading", { level: 2, name: "3 responses" })).toBeTruthy();
    expect(screen.getByText("2 complete · 1 partial · median 4 min")).toBeTruthy();
    expect(screen.getAllByRole("region").map((region) => region.getAttribute("aria-label"))).toEqual([
      "1 How did you hear about us?",
      "2 How satisfied are you overall?",
      "3 What could we improve?",
      "4 How do you rate?",
      "5 Would you recommend us?",
    ]);
    await expectNoAxeViolations(container);
  });

  it("counts choices with their share, in the survey's order", async () => {
    await shown();

    const source = card("1 How did you hear about us?");
    expect(source.getByText("SOURCE · 3 answered")).toBeTruthy();
    expect(source.getAllByRole("listitem").map((item) => item.textContent)).toEqual(["Search engine2 · 67 %", "Advertising0 · 0 %", "Other1 · 33 %"]);
  });

  it("shows the average and the spread of ratings, the NPS, the latest texts and matrix rows", async () => {
    await shown();

    expect(card("2 How satisfied are you overall?").getByText("3.7")).toBeTruthy();
    expect(card("2 How satisfied are you overall?").getByText("average · median 4")).toBeTruthy();
    expect(card("5 Would you recommend us?").getByText("NPS 0")).toBeTruthy();
    expect(card("5 Would you recommend us?").getByText("1 detractors · 0 passives · 1 promoters")).toBeTruthy();
    expect(card("3 What could we improve?").getAllByRole("listitem").map((item) => item.firstChild?.textContent)).toEqual(["Parking was hard to find.", "Shorter waiting times."]);
    expect(card("4 How do you rate?").getByText("Price")).toBeTruthy();
    expect(card("4 How do you rate?").getByText("Good 1 · Poor 1")).toBeTruthy();
  });

  it("filters by status, time and version; the language switch shows the labels in that language", async () => {
    const { client } = await shown();

    await choose("Status", "Partial");
    await choose("Time", "Last 7 days");
    await choose("Version", "Version 1");
    fireEvent.click(screen.getByRole("button", { name: "DE" }));

    await waitFor(() => expect(client.calls.at(-1)?.[0]).toBe("overview"));
    const [, surveyId, filter, language] = client.calls.at(-1)!;
    expect([surveyId, language]).toEqual(["s1", "de"]);
    expect(filter).toMatchObject({ status: "partial", version: 1 });
    expect(typeof (filter as { from?: string }).from).toBe("string");
  });

  it("goes back to Questions with the tabs, and to the survey list with Surveys (W17, W23)", async () => {
    const { onTab, onBack } = await shown();
    const tabs = within(screen.getByRole("navigation", { name: "Survey" }));

    fireEvent.click(tabs.getByRole("link", { name: "Questions" }));
    fireEvent.click(screen.getByRole("link", { name: "Surveys" }));

    expect(tabs.getByRole("link", { name: "Results 248" }).getAttribute("aria-current")).toBe("page");
    expect(onTab).toHaveBeenCalledWith("build");
    expect(onBack).toHaveBeenCalled();
  });

  it("speaks German", async () => {
    await shown(new FakeResultsClient(), "de");

    expect(screen.getByRole("heading", { level: 2, name: "3 Antworten" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Übersicht" }).getAttribute("aria-selected")).toBe("true");
  });
});

describe("Results (AWK-24, W11): responses", () => {
  it("lists the responses newest first on the shared grid and loads more at the end (W13 B)", async () => {
    const { client, container } = await shown();

    fireEvent.click(screen.getByRole("tab", { name: "Responses" }));
    const grid = await screen.findByRole("grid", { name: "Responses" });
    await within(grid).findByText("Advertising");
    expect(within(grid).getAllByRole("columnheader").map((header) => header.textContent)).toEqual(["Started", "Status", "Time", "Lang.", "SOURCE", "SAT", "IMPROVE", "ASPECTS", "NPS"]);
    expect(within(grid).getByRole("columnheader", { name: "Started" }).getAttribute("aria-sort")).toBe("descending");
    expect(within(grid).getAllByRole("row")).toHaveLength(3);
    expect(screen.getByText("2 of 3 loaded")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Load more" }));

    await waitFor(() => expect(within(grid).getAllByRole("row")).toHaveLength(4));
    expect(client.calls.filter(([name]) => name === "responses").map((call) => (call[2] as { cursor?: string }).cursor)).toEqual([undefined, "2"]);
    expect(within(within(grid).getAllByRole("row")[1]!).getAllByRole("gridcell").slice(1).map((cell) => cell.textContent)).toEqual(["Partial", "—", "EN", "Advertising", "2", "", "", ""]);
    expect(screen.getByText("All 3 loaded.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
    await expectNoAxeViolations(container);
  });

  it("sorts by the start time only, oldest first on the same cursor", async () => {
    const { client } = await shown();
    fireEvent.click(screen.getByRole("tab", { name: "Responses" }));
    const grid = await screen.findByRole("grid", { name: "Responses" });
    await within(grid).findByText("Advertising");

    fireEvent.click(within(within(grid).getByRole("columnheader", { name: "Started" })).getByRole("button"));

    await waitFor(() => expect(client.calls.at(-1)?.[2]).toMatchObject({ order: "oldest" }));
    expect(within(within(grid).getByRole("columnheader", { name: "Status" })).queryByRole("button")).toBeNull();
    expect(within(grid).getByRole("columnheader", { name: "Started" }).getAttribute("aria-sort")).toBe("ascending");
  });

  it("opens one response in a side panel, steps to the next one, and closes with Escape", async () => {
    await shown();
    fireEvent.click(screen.getByRole("tab", { name: "Responses" }));
    const grid = await screen.findByRole("grid", { name: "Responses" });
    await within(grid).findByText("Advertising");

    fireEvent.click(within(grid).getAllByRole("row")[1]!);
    const panel = await screen.findByRole("complementary", { name: "Response" });
    await within(panel).findByText("Advertising");
    expect(within(panel).getByText("How did you hear about us?").nextSibling?.textContent).toBe("Advertising");
    expect(within(panel).getByRole("button", { name: "Previous response" }).hasAttribute("disabled")).toBe(true);

    fireEvent.click(within(panel).getByRole("button", { name: "Next response" }));
    await within(panel).findByText("Parking was hard to find.");
    fireEvent.keyDown(panel, { key: "Escape" });

    expect(screen.queryByRole("complementary", { name: "Response" })).toBeNull();
  });

  it("marks the open response in the grid", async () => {
    await shown();
    fireEvent.click(screen.getByRole("tab", { name: "Responses" }));
    const grid = await screen.findByRole("grid", { name: "Responses" });

    fireEvent.click(await within(grid).findByText("Advertising"));

    expect(within(grid).getByText("Advertising").closest("tr")!.getAttribute("aria-current")).toBe("true");
  });

  it("searches the typed answers", async () => {
    const { client } = await shown();
    fireEvent.click(screen.getByRole("tab", { name: "Responses" }));
    await within(await screen.findByRole("grid", { name: "Responses" })).findByText("Advertising");

    fireEvent.change(screen.getByRole("searchbox", { name: "Search answers" }), { target: { value: "radio" } });

    await waitFor(() => expect(within(screen.getByRole("grid", { name: "Responses" })).getAllByRole("row")).toHaveLength(2));
    expect(client.calls.at(-1)?.[2]).toMatchObject({ search: "radio" });
  });

  it("'Show all answers' on a text card opens the responses", async () => {
    await shown();

    fireEvent.click(card("3 What could we improve?").getByRole("button", { name: "Show all 5 answers" }));

    expect(screen.getByRole("tab", { name: "Responses" }).getAttribute("aria-selected")).toBe("true");
  });
});

describe("Results (AWK-24, W11): states", () => {
  it("before publishing, says results come once the survey is published", async () => {
    const client = new FakeResultsClient();
    client.answer = { ...results, published: false, totals: { ...results.totals, responses: 0, complete: 0, partial: 0 }, questions: [] };
    const { onTab } = await shown(client);

    expect(screen.getByText("Results appear once the survey is published.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Back to Edit" }));
    expect(onTab).toHaveBeenCalledWith("build");
  });

  it("without responses, offers the link to share", async () => {
    const client = new FakeResultsClient();
    client.answer = { ...results, totals: { responses: 0, complete: 0, partial: 0, medianSeconds: null, lastAt: null }, questions: [] };
    await shown(client);

    expect(screen.getByText("No responses yet")).toBeTruthy();
    expect(screen.getByText("https://surveys.acme.at/s/s1")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Copy link" })).toBeTruthy();
  });

  it("when loading fails, offers to try again", async () => {
    const client = new FakeResultsClient();
    client.failOverview = true;
    render(<ResultsPage client={client} surveys={new FakeWorkspaceSurveys()} surveyId="s1" surveyLink="" locale="en" onTab={() => undefined} onBack={() => undefined} searchDelayMs={0} timeZone="UTC" />);

    await screen.findByText("Could not load the results");
    client.failOverview = false;
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    await screen.findByRole("heading", { level: 2, name: "3 responses" });
  });
});

describe("Results (AWK-24, W11 C): export", () => {
  const dialog = () => within(screen.getByRole("dialog", { name: "Export responses" }));

  it("exports Excel by default with the results' filter; a job prepares it, then it is ready to download (W14 A)", async () => {
    const { client, container } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Export" }));
    expect((dialog().getByRole("radio", { name: /^Excel/ }) as HTMLInputElement).checked).toBe(true);
    expect(dialog().getByText("2 responses, as filtered")).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(dialog().getByRole("button", { name: "Export" }));

    const download = await dialog().findByRole("link", { name: "Download" });
    expect(download.getAttribute("href")).toBe("/api/app/exports/e1/file");
    expect(download.getAttribute("download")).toBe("customer-satisfaction-2026.xlsx");
    expect(dialog().getByText("Ready.")).toBeTruthy();
    expect(dialog().getByText("2 responses · 2 KB")).toBeTruthy();
    expect(dialog().getByText("Kept until Oct 22, 2026, then deleted. Every download is in the audit log.")).toBeTruthy();
    expect(client.calls.find(([name]) => name === "startExport")).toEqual(["startExport", "s1", { format: "excel", layout: "wide", values: "labels", status: "complete", language: "en" }]);
    await expectNoAxeViolations(container);
  });

  it("speaks of one response in the singular", async () => {
    const client = new FakeResultsClient();
    client.exportSteps = [{ ...readyExport, done: 1, total: 1 }];
    await shown(client);
    fireEvent.click(screen.getByRole("button", { name: "Export" }));

    fireEvent.click(dialog().getByRole("button", { name: "Export" }));

    expect(await dialog().findByText("1 response · 2 KB")).toBeTruthy();
  });

  it("shows how far the export is while it is prepared", async () => {
    const client = new FakeResultsClient();
    client.exportSteps = [{ status: "running", done: 3120, total: 12400 }];
    await shown(client);
    fireEvent.click(screen.getByRole("button", { name: "Export" }));

    fireEvent.click(dialog().getByRole("button", { name: "Export" }));

    expect(await dialog().findByText("3,120 / 12,400 responses")).toBeTruthy();
    expect(dialog().getByText("Preparing…")).toBeTruthy();
    expect(dialog().getByText("You can close this. The file waits under Recent exports, and a notice says when it is ready.")).toBeTruthy();
    expect(dialog().getByRole("progressbar").getAttribute("aria-valuenow")).toBe("25");
  });

  it("says when Askwerk tries again, and when it gave up", async () => {
    const client = new FakeResultsClient();
    client.exportSteps = [{ status: "running", done: 0, total: 12400, attempt: 1, retryAt: "2026-10-15T09:16:00Z" }, { status: "failed", error: "export-failed" }];
    await shown(client);
    fireEvent.click(screen.getByRole("button", { name: "Export" }));

    fireEvent.click(dialog().getByRole("button", { name: "Export" }));

    expect(await dialog().findByText("Not ready yet. Askwerk tries again at 09:16 (try 2 of 5).")).toBeTruthy();
    expect((await dialog().findByRole("alert")).textContent).toBe("Could not prepare the export. Nothing was lost. Try again.");
    fireEvent.click(dialog().getByRole("button", { name: "Try again" }));
    expect(dialog().getByRole("radio", { name: /^Excel/ })).toBeTruthy();
  });

  it("lists the survey's recent exports with Download, progress or failure (W14 A4)", async () => {
    const client = new FakeResultsClient();
    client.recent = [
      exportItem({ id: "e3", status: "running", format: "excel", done: 1, total: 4 }),
      exportItem({ id: "e2", status: "ready", format: "csv", requestedBy: "Reporting server", byApiKey: true, file: { name: "customer-satisfaction-2026.csv", contentType: "text/csv", size: 1993421 } }),
      exportItem({ id: "e1", status: "failed", format: "json", error: "export-failed" }),
    ];
    await shown(client);

    fireEvent.click(screen.getByRole("button", { name: "Export" }));
    const recent = within(await dialog().findByRole("grid", { name: "Recent exports" }));

    expect(recent.getAllByRole("row").slice(1).map((row) => within(row).getAllByRole("gridcell").map((cell) => cell.textContent))).toEqual([
      ["Excel (.xlsx)", "Anna Berger", "Oct 15, 09:12", "", "25 %"],
      ["customer-satisfaction-2026.csv", "Reporting server (API)", "Oct 15, 09:12", "1.9 MB", "Download"],
      ["Askwerk JSON", "Anna Berger", "Oct 15, 09:12", "", "Failed"],
    ]);
    expect(recent.getByRole("link", { name: "Download" }).getAttribute("href")).toBe("/api/app/exports/e2/file");
  });

  it("keeps preparing after the dialog closes, and says when the file is ready (W14 B)", async () => {
    const client = new FakeResultsClient();
    client.exportSteps = [{ status: "running", done: 1, total: 2 }];
    await shown(client);
    fireEvent.click(screen.getByRole("button", { name: "Export" }));
    fireEvent.click(dialog().getByRole("button", { name: "Export" }));
    await dialog().findByText("Preparing…");

    fireEvent.click(dialog().getByRole("button", { name: "Close" }));
    client.exportSteps = [readyExport];
    const notice = within(await screen.findByRole("status", { name: "Export ready" }));

    expect(notice.getByText("customer-satisfaction-2026.xlsx · 2 KB")).toBeTruthy();
    expect(notice.getByRole("link", { name: "Download" }).getAttribute("href")).toBe("/api/app/exports/e1/file");
    fireEvent.click(notice.getByRole("button", { name: "Dismiss" }));
    expect(screen.queryByRole("status", { name: "Export ready" })).toBeNull();
  });

  it("says so when three exports are already being prepared", async () => {
    const client = new FakeResultsClient();
    client.exportAnswer = new ApiError(429, undefined, "too many", { code: "too-many-exports" });
    await shown(client);
    fireEvent.click(screen.getByRole("button", { name: "Export" }));

    fireEvent.click(dialog().getByRole("button", { name: "Export" }));

    expect((await dialog().findByRole("alert")).textContent).toBe("Three exports are already being prepared. Start another one when one is ready.");
  });

  it("offers layout and values under More for CSV and Excel only", async () => {
    const { client } = await shown();
    fireEvent.click(screen.getByRole("button", { name: "Export" }));

    fireEvent.click(dialog().getByRole("radio", { name: /^CSV/ }));
    fireEvent.click(dialog().getByRole("button", { name: "More" }));
    fireEvent.click(dialog().getByRole("radio", { name: "Long: one row per answer" }));
    fireEvent.click(dialog().getByRole("radio", { name: "Codes" }));
    fireEvent.click(dialog().getByRole("button", { name: "Export" }));
    await dialog().findByRole("link", { name: "Download" });

    expect(client.calls.find(([name]) => name === "startExport")?.[2]).toMatchObject({ format: "csv", layout: "long", values: "codes" });
  });

  it("SPSS needs no choices: always one row per response, with codes and labels in the syntax", async () => {
    await shown();
    fireEvent.click(screen.getByRole("button", { name: "Export" }));

    fireEvent.click(dialog().getByRole("radio", { name: /^SPSS/ }));
    fireEvent.click(dialog().getByRole("button", { name: "More" }));

    expect(dialog().queryByRole("radio", { name: "Codes" })).toBeNull();
    expect(dialog().getByText("SPSS, Stata and JSON always have one row per response, with codes.")).toBeTruthy();
  });

  it("tells viewers they read results but do not export them, and closes with Escape", async () => {
    const client = new FakeResultsClient();
    client.exportAnswer = exportNotAllowed();
    await shown(client);
    fireEvent.click(screen.getByRole("button", { name: "Export" }));

    fireEvent.click(dialog().getByRole("button", { name: "Export" }));

    expect((await dialog().findByRole("alert")).textContent).toBe("Your role can read results, but not export them. An owner or admin can change it.");
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});

describe("Participants (AWK-7, W12 D, W35; its own tab since AWK-29)", () => {
  it("shows the funnel and who was invited, with External ID, field, status and the automatic reminders", async () => {
    const { container } = await participantsShown();

    const grid = await screen.findByRole("grid", { name: "Participants" });
    await within(grid).findByText("anna@acme.at · CRM-1");
    expect(within(grid).getAllByRole("row").slice(1).map((row) => within(row).getAllByRole("gridcell")[1]!.textContent)).toEqual(["Anna Bergeranna@acme.at · CRM-1", "Josef Huberjosef@acme.at", "—old@acme.at"]);
    expect(within(grid).getByText("Sales")).toBeTruthy();
    expect(within(grid).getByText("Answered")).toBeTruthy();
    expect(screen.getByText("answered · 33 %")).toBeTruthy();
    expect(screen.getByText("bounced").parentElement?.textContent).toBe("1 bounced");
    expect(screen.getByRole("switch", { name: "Automatic reminders" }).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByText(/^next: .* to 2 people$/)).toBeTruthy();
    await expectNoAxeViolations(container);
  });

  it("pages, sorts, searches and filters on the server (W13 C, W35)", async () => {
    const participants = new FakeParticipantsClient();
    participants.total = 640;
    await participantsShown(participants);
    await screen.findByText("anna@acme.at · CRM-1");

    fireEvent.click(within(screen.getByRole("group", { name: "Status" })).getByRole("button", { name: /^Not answered/ }));
    await waitFor(() => expect(participants.calls.at(-1)).toEqual(["list", "s1", { status: "not-answered", offset: 0, limit: 50 }]));
    fireEvent.change(screen.getByRole("searchbox", { name: "Search name, email, field…" }), { target: { value: "CRM-2" } });
    await waitFor(() => expect(participants.calls.at(-1)).toEqual(["list", "s1", { search: "CRM-2", status: "not-answered", offset: 0, limit: 50 }]));
    fireEvent.click(screen.getByRole("button", { name: "+ Filter" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Value" }), { target: { value: "Sales" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply" }));
    await waitFor(() => expect(participants.calls.at(-1)).toEqual(["list", "s1", { search: "CRM-2", status: "not-answered", field: "department:Sales", offset: 0, limit: 50 }]));
    // W35: plain headers, no sorting.
    expect(within(screen.getByRole("columnheader", { name: "Status" })).queryByRole("button")).toBeNull();
  });

  it("reminds, invites again, exports and saves the chosen to a contacts group", async () => {
    const participants = new FakeParticipantsClient();
    const saved: string[] = [];
    await participantsShown(participants, (name) => saved.push(name));

    fireEvent.click(await screen.findByRole("checkbox", { name: "Select: josef@acme.at" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select: old@acme.at" }));
    const bar = () => within(screen.getByRole("region", { name: "2 chosen" }));
    fireEvent.click(bar().getByRole("button", { name: "Export chosen" }));
    await waitFor(() => expect(saved).toEqual(["participants.csv"]));
    fireEvent.click(bar().getByRole("button", { name: "Add to contacts group" }));
    fireEvent.change(bar().getByRole("textbox", { name: "Group" }), { target: { value: "Key accounts" } });
    fireEvent.click(bar().getByRole("button", { name: "Save" }));

    expect(await screen.findByText("In Contacts: 1 added, 1 updated.")).toBeTruthy();
    expect(participants.calls.find(([name]) => name === "saveToContacts")).toEqual(["saveToContacts", "s1", { participantIds: ["p2", "p3"] }, "Key accounts"]);
    fireEvent.click(await screen.findByRole("checkbox", { name: "Select: josef@acme.at" }));
    fireEvent.click(within(screen.getByRole("region", { name: "1 chosen" })).getByRole("button", { name: "Remind now" }));
    expect(await screen.findByText("Reminder on its way to the 1 chosen who have not answered.")).toBeTruthy();
    participants.emailReady = false;
    fireEvent.click(await screen.findByRole("checkbox", { name: "Select: josef@acme.at" }));
    fireEvent.click(within(screen.getByRole("region", { name: "1 chosen" })).getByRole("button", { name: "Remind now" }));
    expect(await screen.findByText("Email is not set up yet. Download the links and share them another way.")).toBeTruthy();
  });

  it("removes the chosen participants after asking, and says who was kept", async () => {
    const participants = new FakeParticipantsClient();
    participants.total = 640;
    await participantsShown(participants);

    fireEvent.click(await screen.findByRole("checkbox", { name: "Select this page" }));
    fireEvent.click(screen.getByRole("button", { name: /^Select all/ }));
    const bar = within(screen.getByRole("region", { name: /chosen$/ }));
    fireEvent.click(bar.getByRole("button", { name: "Remove" }));
    fireEvent.click(bar.getByRole("button", { name: "Yes, remove" }));

    expect(await screen.findByText(/removed\. 1 kept, because they have responded\.$/)).toBeTruthy();
    expect(participants.calls.find(([name]) => name === "remove")).toEqual(["remove", "s1", { all: true }]);
  });

  it("adds people by typing, and turns the automatic reminders off", async () => {
    const participants = new FakeParticipantsClient();
    await participantsShown(participants);

    fireEvent.click(await screen.findByRole("switch", { name: "Automatic reminders" }));
    await waitFor(() => expect(participants.calls.find(([name]) => name === "setAutoReminders")).toEqual(["setAutoReminders", "s1", false, 7, 2]));
    fireEvent.click(screen.getByRole("button", { name: /^\+ Add people/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: /^Type or paste/ }));

    expect(screen.getByRole("dialog", { name: "Invite participants" })).toBeTruthy();
  });
});

describe("Results for analysts and viewers (AWK-27, W15 rev. 2)", () => {
  it("has no Create group, but Preview with the PDF in the top bar (W23)", async () => {
    const { container } = await shown(new FakeResultsClient(), "en", false);

    expect(screen.queryByRole("link", { name: "Questions" })).toBeNull();
    expect(screen.queryByRole("button", { name: /click to rename/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /^Preview/ }));
    expect(within(screen.getByRole("menu", { name: "Preview" })).getAllByRole("menuitem").map((item) => item.textContent?.replace(/[↗↓]|Ctrl Shift P/g, "").trim())).toEqual(["In a side panel", "In a new tab", "As PDF"]);
    fireEvent.click(screen.getByRole("menuitem", { name: /In a side panel/ }));

    const panel = screen.getByRole("complementary", { name: "Preview" });
    expect(within(panel).getByTitle("Survey preview").getAttribute("src")).toBe("/s/s1?preview");
    expect(within(panel).getByText("Preview · answers are not saved")).toBeTruthy();
    await expectNoAxeViolations(container);
  });

  it("before publishing, says results come later and that Preview shows the survey", async () => {
    const client = new FakeResultsClient();
    client.answer = { ...results, published: false, totals: { responses: 0, complete: 0, partial: 0, medianSeconds: null, lastAt: null }, questions: [] };
    await shown(client, "de", false);

    expect(screen.getByText("Bis dahin zeigt die Vorschau, wie sie aussehen wird.")).toBeTruthy();
    expect(screen.getByRole("button", { name: /^Vorschau/ })).toBeTruthy();
  });

  it("lets readers approve from the Preview panel, without the approval next to Publish (AWK-28, W16)", async () => {
    await shown(new FakeResultsClient(), "en", false);

    expect(screen.queryByRole("button", { name: "Not approved" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /^Preview/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: /In a side panel/ }));
    fireEvent.click(within(screen.getByRole("complementary", { name: "Preview" })).getByRole("button", { name: "Approve…" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Note (optional)" }), { target: { value: "looks right" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect(await screen.findByText(/^✓ Approved by you · /)).toBeTruthy();
  });

  it("keeps Questions for editors, and Preview", async () => {
    await shown();

    expect(screen.getByRole("link", { name: "Questions" })).toBeTruthy();
    expect(screen.getByRole("button", { name: /^Preview/ })).toBeTruthy();
  });
});
