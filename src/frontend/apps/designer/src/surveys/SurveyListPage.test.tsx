// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type Me, type NewSurvey, type SurveyList, type SurveyListQuery, type SurveysClient, type SurveyTemplate } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { themePresets } from "@askwerk/schema";
import { afterEach, describe, expect, it, vi } from "vitest";
import { anna } from "../account/testing";
import { SurveyListPage } from "./SurveyListPage";

afterEach(cleanup);

const now = () => new Date("2026-10-14T12:00:00Z");

const fourSurveys: SurveyList = {
  items: [
    { id: "s1", title: "Customer satisfaction 2026", status: "published", publishedVersion: 3, hasChanges: false, languages: ["de", "en"], createdBy: "Anna Berger", updatedAt: "2026-10-14T09:12:00Z", responses: 248 },
    { id: "s2", title: "Employee pulse Q4", status: "draft", publishedVersion: null, hasChanges: false, languages: ["de"], createdBy: "Anna Berger", updatedAt: "2026-10-13T15:00:00Z", responses: 0 },
    { id: "s3", title: "Event feedback Vienna", status: "published", publishedVersion: 1, hasChanges: true, languages: ["de", "en"], createdBy: "Anna Berger", updatedAt: "2026-10-12T10:00:00Z", responses: 31 },
    { id: "s4", title: "Product survey 2025", status: "closed", publishedVersion: 2, hasChanges: false, languages: ["en"], createdBy: "Anna Berger", updatedAt: "2026-03-03T10:00:00Z", responses: 1204 },
  ],
  counts: { all: 4, draft: 1, published: 2, closed: 1 },
  total: 4,
};

const empty: SurveyList = { items: [], counts: { all: 0, draft: 0, published: 0, closed: 0 }, total: 0 };

class FakeSurveys implements SurveysClient {
  readonly queries: SurveyListQuery[] = [];
  readonly created: NewSurvey[] = [];
  pages: SurveyList[] = [fourSurveys];
  failures = 0;
  createAnswer: string | ApiError = "s9";

  list = (query: SurveyListQuery) => {
    this.queries.push(query);

    if (this.failures > 0) {
      this.failures--;
      return Promise.reject(new Error("offline"));
    }

    return Promise.resolve(this.pages.length > 1 ? this.pages.shift()! : this.pages[0]!);
  };

  create = (survey: NewSurvey) => {
    this.created.push(survey);
    return typeof this.createAnswer === "string" ? Promise.resolve(this.createAnswer) : Promise.reject(this.createAnswer);
  };

  loadDraft = () => Promise.reject(new Error("not used"));
  saveDraft = () => Promise.reject(new Error("not used"));
  publish = () => Promise.reject(new Error("not used"));
  setTheme = () => Promise.reject(new Error("not used"));
  listTemplates = () =>
    Promise.resolve<SurveyTemplate[]>([
      { id: "askwerk:customer-satisfaction", scope: "askwerk", name: null, names: { de: "Kundenzufriedenheit", en: "Customer satisfaction" }, questions: 20, languages: ["de", "en"] },
      { id: "askwerk:nps-short", scope: "askwerk", name: null, names: { de: "NPS, kurz", en: "NPS, short" }, questions: 3, languages: ["de", "en"] },
      { id: "7f1c", scope: "organisation", name: "Acme check-in", names: null, questions: 14, languages: ["de"] },
    ]);
  saveAsTemplate = () => Promise.reject(new Error("not used"));
  deleteTemplate = () => Promise.reject(new Error("not used"));
  approvals = () => Promise.reject(new Error("not used"));
  approve = () => Promise.reject(new Error("not used"));
  withdrawApproval = () => Promise.reject(new Error("not used"));
  summary = () => Promise.reject(new Error("not used"));
  rename = () => Promise.reject(new Error("not used"));
  duplicate = () => Promise.reject(new Error("not used"));
  close = () => Promise.reject(new Error("not used"));
  reopen = () => Promise.reject(new Error("not used"));
  archive = () => Promise.reject(new Error("not used"));
  setClosingDate = () => Promise.reject(new Error("not used"));
  setOneResponse = () => Promise.reject(new Error("not used"));
  links = () => Promise.reject(new Error("not used"));
  look = () => Promise.reject(new Error("not used"));
  setLook = () => Promise.reject(new Error("not used"));
  dropLook = () => Promise.reject(new Error("not used"));
  readonly samplesCreated: [string, string][] = [];
  listSamples = <TTokens,>() =>
    Promise.resolve([
      {
        id: "cafe",
        names: { de: "Café-Besuch", en: "Café visit" },
        kind: { de: "Bildreich", en: "Graphics-rich" },
        features: { de: ["Bildauswahl"], en: ["picture choices"] },
        look: "atelier",
        tokens: themePresets.find((preset) => preset.id === "atelier")!.tokens as TTokens,
        languages: ["de", "en"],
        questions: 7,
        responses: 86,
        preview: { question: { de: "Welchen Kuchen?", en: "Which cake did you try?" }, choices: [{ de: "Strudel", en: "Strudel" }] },
      },
      {
        id: "pulse",
        names: { de: "Wochen-Puls", en: "Quick pulse" },
        kind: { de: "Schlicht", en: "Spartan" },
        features: { de: ["3 Fragen"], en: ["3 questions"] },
        look: "minimal",
        tokens: themePresets.find((preset) => preset.id === "minimal")!.tokens as TTokens,
        languages: ["de", "en"],
        questions: 3,
        responses: 64,
        preview: { question: { de: "Wie war Ihre Woche?", en: "How was your week?" }, choices: [{ de: "Gut", en: "Good" }] },
      },
    ]);
  createSample = (sampleId: string, title: string) => {
    this.samplesCreated.push([sampleId, title]);
    return Promise.resolve("s7");
  };
}

/** Picks an option of a shared combo box (AWK-19): open it by its label, then click the option. */
function choose(scope: { getByRole: typeof screen.getByRole }, label: string, option: string) {
  const button = scope.getByRole("button", { name: new RegExp(`^${label}( |$)`) });
  expect(button.getAttribute("aria-haspopup")).toBe("listbox");
  fireEvent.click(button);
  fireEvent.click(screen.getByRole("option", { name: option }));
}

const viewer: Me = { ...anna, organisations: [{ ...anna.organisations[0]!, role: "viewer" }] };

describe("New survey from a sample (W26)", () => {
  it("shows the samples with their looks, and creates the chosen one under its own name", async () => {
    const client = new FakeSurveys();
    const { onOpen, container } = renderList({ client });
    fireEvent.click(await screen.findByRole("button", { name: "New survey" }));
    const dialog = within(screen.getByRole("dialog", { name: "New survey" }));

    fireEvent.click(dialog.getByRole("radio", { name: "A sample" }));
    const samples = within(await dialog.findByRole("radiogroup", { name: "Samples" }));
    expect(samples.getAllByRole("radio")).toHaveLength(2);
    expect(samples.getByText("86 sample answers", { exact: false })).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(dialog.getByRole("button", { name: "Create" }));
    expect(dialog.getByText("Choose a sample.")).toBeTruthy();
    fireEvent.click(samples.getAllByRole("radio")[0]!);
    fireEvent.click(dialog.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("s7"));
    expect(client.samplesCreated).toEqual([["cafe", "Café visit"]]);
  });
});

class MemoryStorage {
  readonly values = new Map<string, string>();
  get = (key: string) => this.values.get(key) ?? null;
  set = (key: string, value: string) => void this.values.set(key, value);
}

function renderList({ client = new FakeSurveys(), me = anna, locale = "en" as "en" | "de", address = "", storage = new MemoryStorage() } = {}) {
  const onOpen = vi.fn();
  const onOpenResults = vi.fn();
  const onAddress = vi.fn();
  const view = render(
    <SurveyListPage
      client={client}
      me={me}
      locale={locale}
      onOpen={onOpen}
      onOpenResults={onOpenResults}
      address={address}
      onAddress={onAddress}
      storage={storage}
      now={now}
      timeZone="UTC"
      searchDelayMs={0}
    />,
  );

  return { client, onOpen, onOpenResults, onAddress, storage, ...view };
}

const firstPage = { search: "", sort: "-changed", offset: 0, limit: 50 };

describe("SurveyListPage (W6)", () => {
  it("names the organisation whose surveys it shows", async () => {
    renderList();

    expect(screen.getByRole("heading", { level: 1, name: "Surveys" })).toBeTruthy();
    expect(screen.getByText("of Acme GmbH · switch the organisation at the top of the sidebar")).toBeTruthy();
    await screen.findByText("Employee pulse Q4");
  });

  it("shows one row per survey with status, languages, responses and last change", async () => {
    renderList();

    await screen.findByText("Employee pulse Q4");
    const rows = screen.getAllByRole("row").slice(1).map((row) => within(row).getAllByRole("gridcell").map((cell) => cell.textContent));

    expect(rows).toEqual([
      ["Customer satisfaction 2026by Anna Berger", "Published · v3", "DE EN", "248", "today 09:12"],
      ["Employee pulse Q4by Anna Berger", "Draft", "DE", "—", "yesterday"],
      ["Event feedback Viennaby Anna Berger", "Draft · changes since v1", "DE EN", "31", "12 Oct"],
      ["Product survey 2025by Anna Berger", "Closed", "EN", "1,204", "3 Mar"],
    ]);
  });

  it("opens the designer from a row", async () => {
    const { onOpen } = renderList();

    fireEvent.click(await screen.findByRole("link", { name: "Employee pulse Q4" }));

    expect(onOpen).toHaveBeenCalledWith("s2");
  });

  it("opens the results from the response count (AWK-24)", async () => {
    const { onOpen, onOpenResults } = renderList();

    fireEvent.click(await screen.findByRole("button", { name: "248 responses" }));

    expect(onOpenResults).toHaveBeenCalledWith("s1");
    expect(onOpen).not.toHaveBeenCalled();
  });

  it("filters by status, with the counts on the filters", async () => {
    const { client } = renderList();
    await screen.findByText("Employee pulse Q4");

    fireEvent.click(screen.getByRole("button", { name: "Published 2" }));

    await waitFor(() => expect(client.queries.at(-1)).toEqual({ ...firstPage, status: "published" }));
    expect(screen.getByRole("button", { name: "Published 2" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "All 4" }).getAttribute("aria-pressed")).toBe("false");
  });

  it("searches the titles", async () => {
    const { client } = renderList();
    await screen.findByText("Employee pulse Q4");

    fireEvent.change(screen.getByRole("searchbox", { name: "Search surveys" }), { target: { value: "vienna" } });

    await waitFor(() => expect(client.queries.at(-1)).toEqual({ ...firstPage, search: "vienna" }));
  });

  it("pages with numbers, 50 to a page, and keeps the page in the address (W13 A)", async () => {
    const client = new FakeSurveys();
    client.pages = [{ ...fourSurveys, total: 120 }];
    const { onAddress } = renderList({ client });
    await screen.findByText("1–50 of 120");

    fireEvent.click(screen.getByRole("button", { name: "Page 3" }));

    await waitFor(() => expect(client.queries.at(-1)).toEqual({ ...firstPage, offset: 100 }));
    expect(onAddress).toHaveBeenLastCalledWith("?page=3");
    expect(await screen.findByText("101–120 of 120")).toBeTruthy();
  });

  it("sorts by a column on the server and starts again on page 1", async () => {
    const client = new FakeSurveys();
    client.pages = [{ ...fourSurveys, total: 120 }];
    const { onAddress } = renderList({ client, address: "?page=2" });
    await screen.findByText("51–100 of 120");

    fireEvent.click(within(screen.getByRole("columnheader", { name: "Title" })).getByRole("button"));

    await waitFor(() => expect(client.queries.at(-1)).toEqual({ ...firstPage, sort: "title" }));
    expect(onAddress).toHaveBeenLastCalledWith("?sort=title");
    expect(screen.getByRole("columnheader", { name: "Changed" }).getAttribute("aria-sort")).toBeNull();
  });

  it("opens on the page, sort and size in the address", async () => {
    const client = new FakeSurveys();
    renderList({ client, address: "?page=3&sort=-responses&size=25" });

    await waitFor(() => expect(client.queries.at(-1)).toEqual({ search: "", sort: "-responses", offset: 50, limit: 25 }));
    expect(screen.getByRole("columnheader", { name: "Responses" }).getAttribute("aria-sort")).toBe("descending");
  });

  it("remembers the page size on this device", async () => {
    const client = new FakeSurveys();
    client.pages = [{ ...fourSurveys, total: 120 }];
    const { storage, onAddress } = renderList({ client });
    await screen.findByText("1–50 of 120");

    fireEvent.click(screen.getByRole("button", { name: /^Rows per page/ }));
    fireEvent.click(screen.getByRole("option", { name: "100 per page" }));

    await waitFor(() => expect(client.queries.at(-1)).toEqual({ ...firstPage, limit: 100 }));
    expect(storage.get("askwerk.grid.surveys.size")).toBe("100");
    expect(onAddress).toHaveBeenLastCalledWith("?size=100");
    cleanup();
    const again = new FakeSurveys();
    renderList({ client: again, storage });
    await waitFor(() => expect(again.queries.at(-1)).toEqual({ ...firstPage, limit: 100 }));
  });

  it("says when no survey matches, with a way to clear the search (W13 E)", async () => {
    const client = new FakeSurveys();
    client.pages = [fourSurveys, { ...fourSurveys, items: [], total: 0 }, fourSurveys];
    renderList({ client });
    await screen.findByText("Employee pulse Q4");

    fireEvent.change(screen.getByRole("searchbox", { name: "Search surveys" }), { target: { value: "pulse 2025" } });
    expect(await screen.findByText("No survey matches “pulse 2025”.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Clear search" }));

    expect((screen.getByRole("searchbox", { name: "Search surveys" }) as HTMLInputElement).value).toBe("");
    await waitFor(() => expect(client.queries.at(-1)).toEqual(firstPage));
  });

  it("says when the list did not load, and tries again", async () => {
    const client = new FakeSurveys();
    client.failures = 1;
    renderList({ client });

    expect((await screen.findByRole("alert")).textContent).toContain("Not loaded. The server did not answer.");
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("Employee pulse Q4")).toBeTruthy();
  });

  it("has no axe violations with the grid", async () => {
    const { container } = renderList();
    await screen.findByText("Employee pulse Q4");

    await expectNoAxeViolations(container);
  });

  it("invites editors to create the first survey", async () => {
    const client = new FakeSurveys();
    client.pages = [empty];
    renderList({ client });

    expect(await screen.findByRole("heading", { name: "No surveys yet" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Create the first survey" })).toBeTruthy();
  });

  it("tells analysts and viewers who can create one", async () => {
    const client = new FakeSurveys();
    client.pages = [empty];
    renderList({ client, me: viewer });

    expect(await screen.findByText("No surveys yet. Editors and admins of Acme GmbH can create one.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /survey/ })).toBeNull();
  });

  it("creates a survey with title and languages, then opens it", async () => {
    const { client, onOpen } = renderList({ locale: "de" });
    await screen.findByText("Employee pulse Q4");

    fireEvent.click(screen.getByRole("button", { name: "Neue Umfrage" }));
    const dialog = screen.getByRole("dialog", { name: "Neue Umfrage" });
    fireEvent.change(within(dialog).getByLabelText("Titel (intern, nicht für Teilnehmende)"), { target: { value: "Kundenzufriedenheit 2026" } });
    fireEvent.click(within(dialog).getByRole("checkbox", { name: "Englisch" }));
    choose(within(dialog), "Standardsprache", "Englisch");
    fireEvent.click(within(dialog).getByRole("button", { name: "Anlegen" }));

    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("s9"));
    expect(client.created).toEqual([{ title: "Kundenzufriedenheit 2026", languages: ["de", "en"], defaultLanguage: "en" }]);
  });

  it("asks for a title", async () => {
    const client = new FakeSurveys();
    client.createAnswer = new ApiError(400, "t", "failed", { errors: { title: ["title-required"] } });
    renderList({ client });
    await screen.findByText("Employee pulse Q4");

    fireEvent.click(screen.getByRole("button", { name: "New survey" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "New survey" })).getByRole("button", { name: "Create" }));

    expect(await screen.findByText("Enter a title.")).toBeTruthy();
  });

  it("starts a survey from one of Askwerk's templates, which brings its languages", async () => {
    const { client, onOpen } = renderList();
    await screen.findByText("Employee pulse Q4");

    fireEvent.click(screen.getByRole("button", { name: "New survey" }));
    const dialog = within(screen.getByRole("dialog", { name: "New survey" }));
    fireEvent.click(dialog.getByRole("radio", { name: "From a template" }));
    const templates = within(await dialog.findByRole("radiogroup", { name: "Template" }));
    expect(templates.getAllByRole("radio").map((radio) => radio.closest("label")?.textContent)).toEqual([
      "Customer satisfaction20 questions · DE EN",
      "NPS, short3 questions · DE EN",
      "Acme check-in14 questions · DE",
    ]);
    fireEvent.click(templates.getByRole("radio", { name: /NPS, short/ }));
    expect(dialog.queryByRole("group", { name: "Languages" })).toBeNull();
    fireEvent.change(dialog.getByLabelText("Title (internal, not shown to respondents)"), { target: { value: "NPS Q4" } });
    fireEvent.click(dialog.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("s9"));
    expect(client.created).toEqual([{ title: "NPS Q4", languages: [], defaultLanguage: "", templateId: "askwerk:nps-short" }]);
  });

  it("has no accessibility violations with the templates shown", async () => {
    const { container } = renderList();
    await screen.findByText("Employee pulse Q4");
    fireEvent.click(screen.getByRole("button", { name: "New survey" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "New survey" })).getByRole("radio", { name: "From a template" }));
    await screen.findByRole("radiogroup", { name: "Template" });

    await expectNoAxeViolations(container);
  });

  it("does not offer New survey to viewers", async () => {
    renderList({ me: viewer });
    await screen.findByText("Employee pulse Q4");

    expect(screen.queryByRole("button", { name: "New survey" })).toBeNull();
  });

  it("has no accessibility violations", async () => {
    const { container } = renderList();
    await screen.findByText("Employee pulse Q4");

    await expectNoAxeViolations(container);
  });
});
