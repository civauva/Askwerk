// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ApiError } from "@askwerk/app-client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Designer } from "./Designer";
import { FakeDesignerApi, sampleDefinition, sampleDraft, sequentialIds } from "./testing";
import { dropLine, grab, lift, moveTo, placeAt, release } from "./dragTesting";

afterEach(cleanup);

function renderDesigner(api = new FakeDesignerApi(), locale: "de" | "en" = "en") {
  const view = render(<Designer api={api} surveyId="s1" initialLocale={locale} newId={sequentialIds()} autosaveDelayMs={0} />);

  return { api, ...view };
}

async function loaded(api?: FakeDesignerApi, locale: "de" | "en" = "en") {
  const view = renderDesigner(api, locale);
  await screen.findByRole("navigation", { name: locale === "en" ? "Outline" : "Gliederung" });
  // The draft arrives outside act(); flush the effects (keyboard listeners) the load scheduled, or a busy machine
  // lets a test press Ctrl K before the listener exists.
  await act(async () => {});

  return view;
}

const outline = () => within(screen.getByRole("navigation", { name: "Outline" }));

/** Picks an option of a shared combo box (AWK-19): open it by its label, then click the option. */
function choose(scope: { getByRole: typeof screen.getByRole }, label: string, option: string) {
  const button = scope.getByRole("button", { name: new RegExp(`^${label}( |$)`) });
  expect(button.getAttribute("aria-haspopup")).toBe("listbox");
  fireEvent.click(button);
  fireEvent.click(screen.getByRole("option", { name: option }));
}

const inspector = () => within(screen.getByRole("complementary", { name: "Question settings" }));

describe("Designer main screen (M1)", () => {
  it("shows a skeleton while the survey loads, announced as busy", () => {
    renderDesigner(new FakeDesignerApi("pending"));

    expect(screen.getByRole("status", { name: "Loading survey" }).getAttribute("aria-busy")).toBe("true");
  });

  it("shows the survey name, save state, survey languages, missing translations, preview and publish in the top bar", async () => {
    await loaded();
    const bar = within(screen.getByRole("banner"));

    expect(bar.getByText("Customer satisfaction 2026")).toBeTruthy();
    expect(bar.getByText("Draft · saved")).toBeTruthy();
    expect(bar.getByRole("button", { name: /: English$/ })).toBeTruthy();
    expect(bar.queryByRole("button", { name: /: Deutsch$/ })).toBeNull();
    expect(bar.getByText("3 missing")).toBeTruthy();
    expect(bar.getByRole("button", { name: "Preview" })).toBeTruthy();
    expect(bar.getByRole("button", { name: "Publish" })).toBeTruthy();
  });

  it("speaks German when the browser does, and edits German texts first", async () => {
    await loaded(new FakeDesignerApi(), "de");
    const bar = within(screen.getByRole("banner"));

    expect(bar.getByText("Entwurf · gespeichert")).toBeTruthy();
    expect(bar.getByRole("button", { name: /: Deutsch$/ })).toBeTruthy();
    expect(bar.getByText("3 fehlen")).toBeTruthy();
    expect(bar.getByRole("button", { name: "Veröffentlichen" })).toBeTruthy();
  });

  it("counts a single missing translation in the singular", async () => {
    const oneMissing = { ...sampleDraft, definition: { ...sampleDraft.definition, pages: [sampleDraft.definition.pages[0]!] } };

    await loaded(new FakeDesignerApi(oneMissing), "de");

    expect(within(screen.getByRole("banner")).getByText("1 fehlt")).toBeTruthy();
  });

  it("lists the pages and their questions with variable names in the outline", async () => {
    await loaded();

    expect(outline().getByText("01 · Welcome")).toBeTruthy();
    expect(outline().getByText("02 · Your visit")).toBeTruthy();
    expect(outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" })).toBeTruthy();
  });

  it("selects a question from the outline and shows its settings in the inspector", async () => {
    await loaded();

    fireEvent.click(outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" }));

    expect(outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" }).getAttribute("aria-current")).toBe("true");
    expect((inspector().getByRole("textbox", { name: "Variable name" }) as HTMLInputElement).value).toBe("SAT_1");
    expect(inspector().getByRole("switch", { name: "Required" }).getAttribute("aria-checked")).toBe("true");
  });

  it("edits the selected question's text on the canvas and saves the draft", async () => {
    const { api } = await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    fireEvent.change(screen.getByRole("textbox", { name: "Question text" }), { target: { value: "Which branch did you visit?" } });

    expect(outline().getByRole("button", { name: "Which branch did you visit? BRANCH" })).toBeTruthy();
    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions[0]?.text).toEqual({ de: "Welche Filiale?", en: "Which branch did you visit?" }));
  });

  it("switches the survey language and marks texts that are not translated yet", async () => {
    await loaded();

    fireEvent.click(within(screen.getByRole("banner")).getByRole("button", { name: /: (English|Deutsch)$/ }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: /Deutsch/ }));

    expect(screen.getByRole("heading", { name: "Wie zufrieden sind Sie mit unserem Service?" })).toBeTruthy();
    const improve = screen.getByRole("article", { name: "Q3 · IMPROVE" });
    expect(within(improve).getByText("Translation missing")).toBeTruthy();
    expect(within(improve).getByText("English: “What could we improve?”")).toBeTruthy();
  });

  it("turns the required flag on and off in the inspector", async () => {
    const { api } = await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    fireEvent.click(inspector().getByRole("switch", { name: "Required" }));

    expect(inspector().getByRole("switch", { name: "Required" }).getAttribute("aria-checked")).toBe("true");
    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions[0]?.required).toBe(true));
  });

  it("flags a key question: a star in the outline, saved in the definition, and off again (W38)", async () => {
    const { api, container } = await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    fireEvent.click(inspector().getByRole("switch", { name: "Key question" }));

    expect(outline().getByRole("button", { name: "Which branch? Key question BRANCH" })).toBeTruthy();
    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions[0]?.key).toBe(true));
    await expectNoAxeViolations(container);
    fireEvent.click(inspector().getByRole("switch", { name: "Key question" }));
    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions[0]).not.toHaveProperty("key"));
  });

  it("draws Required like every other switch in the inspector", async () => {
    await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    const required = inspector().getByRole("switch", { name: "Required" });
    const randomise = inspector().getByRole("switch", { name: "Randomise options" });

    expect(required.className).toBe(randomise.className);
  });

  it("explains an invalid variable name right at the field", async () => {
    await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    fireEvent.change(inspector().getByRole("textbox", { name: "Variable name" }), { target: { value: "branch 1" } });

    expect(inspector().getByText("Use A–Z, 0–9 and _, starting with a letter.")).toBeTruthy();
    expect(inspector().getByRole("textbox", { name: "Variable name" }).getAttribute("aria-invalid")).toBe("true");
  });

  it("shows the grid behind the canvas until it is switched off", async () => {
    await loaded();
    const grid = screen.getByRole("switch", { name: "Grid" });
    const canvas = screen.getByRole("main", { name: "Canvas" });
    const before = [grid.getAttribute("aria-checked"), canvas.classList.contains("designer-canvas--grid")];

    fireEvent.click(grid);

    expect(before).toEqual(["true", true]);
    expect(grid.getAttribute("aria-checked")).toBe("false");
    expect(canvas.classList.contains("designer-canvas--grid")).toBe(false);
  });

  it.each([["en"], ["de"]] as const)("has no accessibility violations in %s", async (locale) => {
    const { container } = await loaded(new FakeDesignerApi(), locale);
    await act(() => Promise.resolve());

    await expectNoAxeViolations(container);
  });
});

describe("Adding and arranging questions (M1, M2)", () => {
  it("adds a question at the end of a page through the type menu, filtered by typing, and selects it", async () => {
    const { api } = await loaded();
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[1]!);

    fireEvent.change(screen.getByRole("combobox", { name: "Question type" }), { target: { value: "rat" } });
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Question type" }), { key: "Enter" });

    expect(screen.queryByRole("listbox", { name: "Question type" })).toBeNull();
    expect(outline().getByRole("button", { name: "Untitled question Q7" }).getAttribute("aria-current")).toBe("true");
    expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Question text" }));
    await waitFor(() => expect(api.saved.at(-1)?.pages[1]?.questions.at(-1)).toEqual({
      id: "new1",
      variable: "Q7",
      type: "rating",
      text: {},
      required: false,
      scale: { min: 1, max: 5 },
    }));
  });

  it("shows only the types that match the filter and highlights the first", async () => {
    await loaded();
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);

    fireEvent.change(screen.getByRole("combobox", { name: "Question type" }), { target: { value: "text" } });

    const options = within(screen.getByRole("listbox", { name: "Question type" })).getAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual(["Short text 5", "Long text 6", "Text block"]);
    expect(options[0]?.getAttribute("aria-selected")).toBe("true");
  });

  it("moves the highlight with the arrow keys", async () => {
    await loaded();
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);

    fireEvent.keyDown(screen.getByRole("combobox", { name: "Question type" }), { key: "ArrowDown" });

    expect(screen.getByRole("option", { name: "Multiple choice 2" }).getAttribute("aria-selected")).toBe("true");
  });

  it("picks a type with its number key", async () => {
    await loaded();
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);

    fireEvent.keyDown(screen.getByRole("combobox", { name: "Question type" }), { key: "4" });

    expect(outline().getByRole("button", { name: "Untitled question Q7" })).toBeTruthy();
    expect(within(screen.getByRole("article", { name: "Q2 · Q7" })).getByText("NET PROMOTER SCORE · Q7")).toBeTruthy();
  });

  it("closes the type menu with Escape without adding anything", async () => {
    await loaded();
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);

    fireEvent.keyDown(screen.getByRole("combobox", { name: "Question type" }), { key: "Escape" });

    expect(screen.queryByRole("listbox")).toBeNull();
    expect(outline().queryByRole("button", { name: "Untitled question Q7" })).toBeNull();
  });

  it("inserts a question right after the selected one", async () => {
    await loaded();
    fireEvent.click(outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" }));

    fireEvent.click(screen.getByRole("button", { name: "Insert question here" }));
    fireEvent.click(screen.getByRole("option", { name: "Long text 6" }));

    const visit = outline().getAllByRole("listitem").map((item) => item.textContent);
    expect(visit.slice(1, 4)).toEqual(["How satisfied are you with our service? SAT_1", "Untitled question Q7", "What could we improve? IMPROVE"]);
  });

  it("duplicates the selected question and selects the copy", async () => {
    await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    fireEvent.click(screen.getByRole("button", { name: "Duplicate" }));

    expect(outline().getByRole("button", { name: "Which branch? Q7" }).getAttribute("aria-current")).toBe("true");
  });

  it("deletes the selected question", async () => {
    const { api } = await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    expect(outline().queryByRole("button", { name: "Which branch? BRANCH" })).toBeNull();
    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions.map((question) => question.variable)).toEqual(["AGE"]));
  });

  it.each([
    ["en", "No questions yet.", "Add the first question"],
    ["de", "Noch keine Fragen.", "Erste Frage hinzufügen"],
  ] as const)("shows the empty survey in %s with a way to add the first question", async (locale, heading, button) => {
    const empty = { ...sampleDraft, title: "New survey", definition: { ...sampleDraft.definition, pages: [{ id: "p1", questions: [] }] } };
    await loaded(new FakeDesignerApi(empty), locale);

    fireEvent.click(screen.getByRole("button", { name: button }));

    expect(screen.getByRole("heading", { name: heading })).toBeTruthy();
    expect(screen.getByRole("listbox")).toBeTruthy();
  });

  it("has no accessibility violations with the type menu open", async () => {
    const { container } = await loaded();
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);

    await expectNoAxeViolations(container);
  });
});

describe("Saving and translating (M2)", () => {
  const failure = new ApiError(503, "4bf92f3577b34da6a3ce929d0e0e4736", "save failed");

  it("says plainly when a change could not be saved, keeps it on screen and gives the reference", async () => {
    await loaded(new FakeDesignerApi(undefined, [failure]));
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));

    fireEvent.change(screen.getByRole("textbox", { name: "Question text" }), { target: { value: "Which branch did you visit?" } });

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByRole("heading", { name: "Your last change could not be saved." })).toBeTruthy();
    expect(within(alert).getByText("Reference 4bf92f3577b34da6a3ce929d0e0e4736")).toBeTruthy();
    expect(within(screen.getByRole("banner")).getByText("Not saved · retrying in 8 s")).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "Question text" }) as HTMLInputElement).value).toBe("Which branch did you visit?");
  });

  it("saves again at once with Retry now", async () => {
    await loaded(new FakeDesignerApi(undefined, [failure, "ok"]));
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Question text" }), { target: { value: "Which branch did you visit?" } });

    fireEvent.click(within(await screen.findByRole("alert")).getByRole("button", { name: "Retry now" }));

    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    expect(within(screen.getByRole("banner")).getByText("Draft · saved")).toBeTruthy();
  });

  it("marks questions without a translation in the outline", async () => {
    await loaded(new FakeDesignerApi(), "de");

    expect(within(screen.getByRole("navigation", { name: "Gliederung" })).getByRole("button", { name: "What could we improve? Übersetzung fehlt IMPROVE" })).toBeTruthy();
  });

  it("shows translation progress per language and the missing texts in the inspector", async () => {
    await loaded();
    fireEvent.click(outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" }));

    fireEvent.click(inspector().getByRole("tab", { name: "Translations" }));

    expect(inspector().getByText("German 12 of 15 texts · English 15 of 15")).toBeTruthy();
    const missing = inspector().getByRole("list", { name: "Missing in German" });
    expect(within(missing).getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "What could we improve? Q3",
      "Age group Q6",
      "Thank you. END",
    ]);
  });

  it("jumps to a question from the list of missing texts", async () => {
    await loaded();
    fireEvent.click(outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" }));
    fireEvent.click(inspector().getByRole("tab", { name: "Translations" }));

    fireEvent.click(inspector().getByRole("button", { name: "Age group Q6" }));

    expect(outline().getByRole("button", { name: "Age group AGE" }).getAttribute("aria-current")).toBe("true");
  });

  it("has no accessibility violations when saving failed", async () => {
    const { container } = await loaded(new FakeDesignerApi(undefined, [failure]));
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Question text" }), { target: { value: "Which branch did you visit?" } });
    await screen.findByRole("alert");

    await expectNoAxeViolations(container);
  });
});

describe("Publishing (M3)", () => {
  const publishButton = () => within(screen.getByRole("banner")).getByRole("button", { name: /^Publish( changes)?$/ });
  const dialog = () => within(screen.getByRole("dialog"));
  const previouslyPublished = {
    ...sampleDefinition,
    pages: sampleDefinition.pages.map((page) => ({
      ...page,
      questions: page.questions.map((question) => (question.id === "q2" ? { ...question, variable: "SATISFACTION" } : question)),
    })),
  };

  it("lists what was checked before publishing", async () => {
    await loaded();

    fireEvent.click(publishButton());

    expect(screen.getByRole("dialog", { name: "Publish “Customer satisfaction 2026”" })).toBeTruthy();
    expect(dialog().getByText("Publish · version 3")).toBeTruthy();
    expect(dialog().getByText("6 questions on 3 pages are complete.")).toBeTruthy();
    expect(dialog().getByText("Variable names are unique.")).toBeTruthy();
  });

  it("names a renamed variable and its mapping", async () => {
    await loaded(new FakeDesignerApi({ ...sampleDraft, published: previouslyPublished }));

    fireEvent.click(publishButton());

    expect(dialog().getByText("Variable names are unique; 1 rename is mapped (SATISFACTION → SAT_1).")).toBeTruthy();
    expect(dialog().getByText("Version 3 is frozen once published. Answers collected so far stay with version 2.")).toBeTruthy();
  });

  it("warns in large type about missing translations and still allows publishing", async () => {
    await loaded();

    fireEvent.click(publishButton());

    const warning = within(dialog().getByRole("region", { name: "3 texts are missing in German" }));
    expect(warning.getByText("Respondents who choose German will see these texts in English:")).toBeTruthy();
    expect(warning.getAllByRole("listitem").map((item) => item.textContent)).toEqual(["What could we improve? (Q3)", "Age group (Q6)", "Thank you. (END)"]);
    expect(dialog().getByRole("button", { name: "Publish with missing translations" }).hasAttribute("disabled")).toBe(false);
  });

  it("takes you to the missing translations", async () => {
    await loaded();
    fireEvent.click(publishButton());

    fireEvent.click(dialog().getByRole("button", { name: "Fix translations" }));

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(within(screen.getByRole("banner")).getByRole("button", { name: /: Deutsch$/ })).toBeTruthy();
  });

  it("blocks publishing while there are errors and says which", async () => {
    await loaded();
    fireEvent.click(outline().getByRole("button", { name: "Which branch? BRANCH" }));
    fireEvent.change(inspector().getByRole("textbox", { name: "Variable name" }), { target: { value: "branch 1" } });

    fireEvent.click(publishButton());

    expect(dialog().getByText("Variable name “branch 1” is not valid (Q5).")).toBeTruthy();
    expect(dialog().getByRole("button", { name: "Publish with missing translations" }).hasAttribute("disabled")).toBe(true);
  });

  it("publishes and shows the survey links", async () => {
    const { api } = await loaded();
    fireEvent.click(publishButton());

    fireEvent.click(dialog().getByRole("button", { name: "Publish with missing translations" }));

    expect(await screen.findByRole("dialog", { name: "Published. Responses are being collected." })).toBeTruthy();
    expect(dialog().getByText("https://surveys.acme.at/s/kz26b7mq")).toBeTruthy();
    expect(api.publishCalls).toBe(1);
  });

  it("copies a survey link", async () => {
    const copied: string[] = [];
    const api = new FakeDesignerApi();
    render(
      <Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} writeClipboard={(text) => {
        copied.push(text);
        return Promise.resolve();
      }} />,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Publish" }));
    fireEvent.click(dialog().getByRole("button", { name: "Publish with missing translations" }));
    await screen.findByRole("dialog", { name: "Published. Responses are being collected." });

    fireEvent.click(dialog().getByRole("button", { name: "Copy https://surveys.acme.at/s/kz26b7mq" }));

    expect(copied).toEqual(["https://surveys.acme.at/s/kz26b7mq"]);
    expect(await dialog().findByText("Copied")).toBeTruthy();
  });

  it("says publishing failed, that nothing changed, and gives the reference", async () => {
    const failure = new ApiError(500, "a3ce929d0e0e4736a3ce929d0e0e4736", "publish failed");
    await loaded(new FakeDesignerApi({ ...sampleDraft, published: previouslyPublished }, ["ok"], [failure]));
    fireEvent.click(publishButton());

    fireEvent.click(dialog().getByRole("button", { name: "Publish with missing translations" }));

    expect(await screen.findByRole("alertdialog", { name: "Publishing failed." })).toBeTruthy();
    expect(screen.getByText("Nothing changed: version 2 stays live and your draft is kept.")).toBeTruthy();
    expect(screen.getByText("Reference a3ce929d0e0e4736a3ce929d0e0e4736")).toBeTruthy();
  });

  it("tries publishing again", async () => {
    const failure = new ApiError(500, "a3ce929d0e0e4736a3ce929d0e0e4736", "publish failed");
    const api = new FakeDesignerApi(sampleDraft, ["ok"], [failure, { versionNumber: 3, links: [] }]);
    await loaded(api);
    fireEvent.click(publishButton());
    fireEvent.click(dialog().getByRole("button", { name: "Publish with missing translations" }));

    fireEvent.click(await screen.findByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("dialog", { name: "Published. Responses are being collected." })).toBeTruthy();
    expect(api.publishCalls).toBe(2);
  });

  it("closes the check with Cancel and with Escape", async () => {
    await loaded();
    fireEvent.click(publishButton());
    fireEvent.click(dialog().getByRole("button", { name: "Cancel" }));
    const afterCancel = screen.queryByRole("dialog");
    fireEvent.click(publishButton());

    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });

    expect(afterCancel).toBeNull();
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it.each([["check"], ["published"]] as const)("has no accessibility violations in the %s dialog", async (step) => {
    const { container } = await loaded();
    fireEvent.click(publishButton());
    if (step === "published") {
      fireEvent.click(dialog().getByRole("button", { name: "Publish with missing translations" }));
      await screen.findByRole("dialog", { name: "Published. Responses are being collected." });
    }

    await expectNoAxeViolations(container);
  });
});

describe("Text blocks (intro)", () => {
  it("shows a text block as text to read, without an answer field", async () => {
    await loaded();

    const intro = within(screen.getByRole("article", { name: "Text · INTRO" }));

    expect(intro.getByText("Welcome. The survey takes about 3 minutes.")).toBeTruthy();
    expect(intro.queryByText("Your answer")).toBeNull();
  });

  it("offers no Required switch for a text block", async () => {
    await loaded();

    fireEvent.click(outline().getByRole("button", { name: "Welcome. The survey takes about 3 minutes. INTRO" }));

    expect(inspector().queryByRole("switch", { name: "Required" })).toBeNull();
    expect(screen.getByRole("textbox", { name: "Text" })).toBeTruthy();
  });

  it("adds a text block from the type menu", async () => {
    await loaded();
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);

    fireEvent.change(screen.getByRole("combobox", { name: "Question type" }), { target: { value: "block" } });
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Question type" }), { key: "Enter" });

    expect(screen.getByRole("article", { name: "Text · Q7" })).toBeTruthy();
  });
});

describe("Inspector settings (M1)", () => {
  const select = (name: string) => fireEvent.click(outline().getByRole("button", { name }));

  it("changes the question type", async () => {
    await loaded();
    select("Which branch? BRANCH");

    choose(inspector(), "Type", "Multiple choice");

    expect(screen.getByText("MULTIPLE CHOICE · BRANCH")).toBeTruthy();
  });

  it("edits, adds and removes options in the survey language", async () => {
    const { api } = await loaded();
    select("Which branch? BRANCH");
    const options = () => within(inspector().getByRole("group", { name: "Options" }));

    fireEvent.change(options().getByRole("textbox", { name: "Option 2" }), { target: { value: "Graz-Mitte" } });
    fireEvent.click(options().getByRole("button", { name: "Add option" }));
    fireEvent.change(options().getByRole("textbox", { name: "Code of option 3" }), { target: { value: "LNZ" } });
    fireEvent.click(options().getByRole("button", { name: "Remove option 1" }));

    await waitFor(() =>
      expect(api.saved.at(-1)?.pages[2]?.questions[0]?.options).toEqual([
        { code: "2", label: { de: "Graz", en: "Graz-Mitte" } },
        { code: "LNZ", label: {} },
      ]),
    );
  });

  it("edits the rows of a matrix", async () => {
    await loaded();
    select("Which branch? BRANCH");
    choose(inspector(), "Type", "Matrix");

    fireEvent.change(within(inspector().getByRole("group", { name: "Rows" })).getByRole("textbox", { name: "Row 1" }), { target: { value: "Service" } });

    expect((within(inspector().getByRole("group", { name: "Rows" })).getByRole("textbox", { name: "Row 1" }) as HTMLInputElement).value).toBe("Service");
  });

  it("sets the scale and its end labels", async () => {
    const { api } = await loaded();
    select("How satisfied are you with our service? SAT_1");

    choose(inspector(), "Scale", "0 to 10");
    fireEvent.change(inspector().getByRole("textbox", { name: "Label at 10" }), { target: { value: "Extremely satisfied" } });

    await waitFor(() =>
      expect(api.saved.at(-1)?.pages[1]?.questions[0]?.scale).toEqual({
        min: 0,
        max: 10,
        minLabel: { de: "Sehr unzufrieden", en: "Very unsatisfied" },
        maxLabel: { de: "Sehr zufrieden", en: "Extremely satisfied" },
      }),
    );
  });

  it("switches randomised options and the Don't know answer", async () => {
    const { api } = await loaded();
    select("Which branch? BRANCH");

    fireEvent.click(inspector().getByRole("switch", { name: "Randomise options" }));
    fireEvent.click(inspector().getByRole("switch", { name: "Show “Don't know”" }));

    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions[0]).toMatchObject({ randomize: true, dontKnow: true }));
  });

  it("offers Randomise only for questions with options", async () => {
    await loaded();

    select("What could we improve? IMPROVE");

    expect(inspector().queryByRole("switch", { name: "Randomise options" })).toBeNull();
  });

  it("has no accessibility violations with options and scale settings", async () => {
    const { container } = await loaded();
    select("Which branch? BRANCH");

    await expectNoAxeViolations(container);
  });
});

describe("Outline search and moving questions (M1)", () => {
  const outlineQuestions = () =>
    outline()
      .getAllByRole("listitem")
      .map((item) => item.querySelector("code")?.textContent);

  it("filters the outline by question text or variable name", async () => {
    await loaded();

    fireEvent.change(outline().getByRole("searchbox", { name: "Search questions" }), { target: { value: "bran" } });

    expect(outlineQuestions()).toEqual(["BRANCH"]);
  });

  it("finds a question by its variable name and says when nothing matches", async () => {
    await loaded();
    const search = outline().getByRole("searchbox", { name: "Search questions" });

    fireEvent.change(search, { target: { value: "nps" } });
    const byVariable = outlineQuestions();
    fireEvent.change(search, { target: { value: "zzz" } });

    expect(byVariable).toEqual(["NPS"]);
    expect(outline().getByText("No questions match.")).toBeTruthy();
  });

  it("jumps to the search with the / key", async () => {
    await loaded();

    fireEvent.keyDown(document.body, { key: "/" });

    expect(document.activeElement).toBe(outline().getByRole("searchbox", { name: "Search questions" }));
  });

  it("lifts a dragged question, marks where it lands, before or after by the pointer, and drops it there (W34)", async () => {
    const { api } = await loaded();
    const age = outline().getByRole("button", { name: "Age group AGE" });
    const target = outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" });
    placeAt(target, 100);

    grab(age);
    expect(lift()).toBeNull();
    moveTo(110);
    expect(age.getAttribute("data-lifted")).toBe("true");
    expect(lift()?.textContent).toBe("Age group");
    expect(target.previousElementSibling).toBe(dropLine());
    moveTo(135);
    expect(target.nextElementSibling).toBe(dropLine());
    release();

    expect(outlineQuestions()).toEqual(["INTRO", "SAT_1", "AGE", "IMPROVE", "NPS", "BRANCH"]);
    expect(dropLine()).toBeNull();
    expect(lift()).toBeNull();
    expect(screen.getByText("Moved to page 2, position 2.")).toBeTruthy();
    await waitFor(() => expect(api.saved.at(-1)?.pages[1]?.questions.map((question) => question.variable)).toEqual(["SAT_1", "AGE", "IMPROVE", "NPS"]));
  });

  it("drops a question between pages, at the start of the next page (W34)", async () => {
    const { api } = await loaded();
    const heading = outline().getByRole("heading", { name: /About you/ });
    placeAt(heading, 300, 24);

    grab(outline().getByRole("button", { name: "Would you recommend us? NPS" }));
    moveTo(310);
    expect(heading.nextElementSibling).toBe(dropLine());
    release();

    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions.map((question) => question.variable)).toEqual(["NPS", "BRANCH", "AGE"]));
  });

  it("drops a question onto an empty page (W34)", async () => {
    const { api } = await loaded();
    fireEvent.click(outline().getByRole("button", { name: "+ Add page" }));
    const empty = outline().getByRole("heading", { name: /^04/ });
    placeAt(empty, 400, 24);

    grab(outline().getByRole("button", { name: "Age group AGE" }));
    moveTo(405);
    release();

    await waitFor(() => expect(api.saved.at(-1)?.pages[3]?.questions.map((question) => question.variable)).toEqual(["AGE"]));
    expect(within(empty.closest("section")!).getByRole("button", { name: "Age group AGE" })).toBeTruthy();
  });

  it("cancels a drag with Escape: nothing moves (W34)", async () => {
    await loaded();
    const age = outline().getByRole("button", { name: "Age group AGE" });
    placeAt(outline().getByRole("button", { name: "How satisfied are you with our service? SAT_1" }), 100);

    grab(age);
    moveTo(110);
    expect(dropLine()).not.toBeNull();
    fireEvent.keyDown(age, { key: "Escape" });
    expect(dropLine()).toBeNull();
    expect(lift()).toBeNull();
    release();

    expect(outlineQuestions()).toEqual(["INTRO", "SAT_1", "IMPROVE", "NPS", "BRANCH", "AGE"]);
    expect(age.getAttribute("data-lifted")).toBeNull();
    expect(screen.getByText("Move cancelled.")).toBeTruthy();
  });

  it("selects a question on a plain click, without a drag", async () => {
    await loaded();
    const age = outline().getByRole("button", { name: "Age group AGE" });

    grab(age);
    release();
    fireEvent.click(age);

    expect(age.getAttribute("aria-current")).toBe("true");
    expect(dropLine()).toBeNull();
  });

  it("moves a question on the canvas by its grip, with the same lift and marker (W34)", async () => {
    await loaded();
    const canvas = within(screen.getByRole("main", { name: "Canvas" }));
    const grip = canvas.getByRole("button", { name: /^Move .*AGE$/ });
    const target = canvas.getByRole("article", { name: /SAT_1$/ });
    placeAt(target, 100, 120);

    grab(grip);
    moveTo(120);
    expect(lift()?.textContent).toBe("Age group");
    expect(grip.closest("article")?.getAttribute("data-lifted")).toBe("true");
    expect(target.previousElementSibling).toBe(dropLine());
    release();

    expect(outlineQuestions()).toEqual(["INTRO", "AGE", "SAT_1", "IMPROVE", "NPS", "BRANCH"]);
  });

  it("moves a question on the canvas with Alt and the arrows on its grip, keeping the focus there", async () => {
    await loaded();
    const canvas = within(screen.getByRole("main", { name: "Canvas" }));

    fireEvent.keyDown(canvas.getByRole("button", { name: /^Move .*BRANCH$/ }), { key: "ArrowDown", altKey: true });

    expect(outlineQuestions()).toEqual(["INTRO", "SAT_1", "IMPROVE", "NPS", "AGE", "BRANCH"]);
    expect(document.activeElement).toBe(canvas.getByRole("button", { name: /^Move .*BRANCH$/ }));
  });

  it("moves the last question of a page to the start of the next page with Alt and the down arrow", async () => {
    const { api } = await loaded();

    fireEvent.keyDown(outline().getByRole("button", { name: "Would you recommend us? NPS" }), { key: "ArrowDown", altKey: true });

    await waitFor(() => expect(api.saved.at(-1)?.pages[2]?.questions.map((question) => question.variable)).toEqual(["NPS", "BRANCH", "AGE"]));
  });

  it("moves a question down and back up with Alt and the arrow keys", async () => {
    await loaded();
    const branch = () => outline().getByRole("button", { name: "Which branch? BRANCH" });

    fireEvent.keyDown(branch(), { key: "ArrowDown", altKey: true });
    const movedDown = outlineQuestions();
    fireEvent.keyDown(branch(), { key: "ArrowUp", altKey: true });

    expect(movedDown).toEqual(["INTRO", "SAT_1", "IMPROVE", "NPS", "AGE", "BRANCH"]);
    expect(outlineQuestions()).toEqual(["INTRO", "SAT_1", "IMPROVE", "NPS", "BRANCH", "AGE"]);
  });
});

describe("Command palette (Ctrl K) and translating flow (M1, M2)", () => {
  const palette = () => within(screen.getByRole("dialog", { name: "Commands" }));
  const run = (text: string) => {
    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });
    fireEvent.change(palette().getByRole("combobox", { name: "Command" }), { target: { value: text } });
    fireEvent.keyDown(palette().getByRole("combobox", { name: "Command" }), { key: "Enter" });
  };

  it("opens with Ctrl K and with Search in the top bar", async () => {
    await loaded();

    fireEvent.click(within(screen.getByRole("banner")).getByRole("button", { name: "Search" }));

    expect(palette().getByRole("combobox", { name: "Command" })).toBeTruthy();
  });

  it("goes to a question by its text or variable", async () => {
    await loaded();

    run("age");

    expect(screen.queryByRole("dialog", { name: "Commands" })).toBeNull();
    expect(outline().getByRole("button", { name: "Age group AGE" }).getAttribute("aria-current")).toBe("true");
  });

  it("adds a question of a type", async () => {
    await loaded();

    run("add rating");

    expect(outline().getByRole("button", { name: "Untitled question Q7" }).getAttribute("aria-current")).toBe("true");
  });

  it("switches the survey language", async () => {
    await loaded();

    run("german");

    expect(within(screen.getByRole("banner")).getByRole("button", { name: /: Deutsch$/ })).toBeTruthy();
  });

  it("opens the publish check", async () => {
    await loaded();

    run("publish");

    expect(screen.getByRole("dialog", { name: "Publish “Customer satisfaction 2026”" })).toBeTruthy();
  });

  it("switches the grid", async () => {
    await loaded();

    run("grid");

    expect(screen.getByRole("switch", { name: "Grid" }).getAttribute("aria-checked")).toBe("false");
  });

  it("closes with Escape", async () => {
    await loaded();
    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });

    fireEvent.keyDown(palette().getByRole("combobox", { name: "Command" }), { key: "Escape" });

    expect(screen.queryByRole("dialog", { name: "Commands" })).toBeNull();
  });

  it("jumps to the next missing translation with Tab while translating", async () => {
    await loaded(new FakeDesignerApi(), "de");
    fireEvent.click(within(screen.getByRole("navigation", { name: "Gliederung" })).getByRole("button", { name: "What could we improve? Übersetzung fehlt IMPROVE" }));
    const text = screen.getByRole("textbox", { name: "Fragetext" });

    fireEvent.change(text, { target: { value: "Was können wir verbessern?" } });
    fireEvent.keyDown(text, { key: "Tab" });

    expect(within(screen.getByRole("navigation", { name: "Gliederung" })).getByRole("button", { name: "Age group Übersetzung fehlt AGE" }).getAttribute("aria-current")).toBe("true");
    expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Fragetext" }));
    expect(screen.getByText("Tab · nächste fehlende")).toBeTruthy();
  });

  it("has no accessibility violations with the palette open", async () => {
    const { container } = await loaded();
    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });

    await expectNoAxeViolations(container);
  });
});

describe("Back to the survey list (AWK-15 step 3)", () => {
  it("leads back from Surveys in the bar", async () => {
    const onBack = vi.fn();
    render(<Designer api={new FakeDesignerApi()} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} onBack={onBack} />);

    fireEvent.click(await screen.findByRole("link", { name: "Surveys" }));

    expect(onBack).toHaveBeenCalled();
  });
});

describe("Save as template (AWK-17 step 5, W10 C)", () => {
  it("saves the survey as the organisation's template from the command palette", async () => {
    const api = new FakeDesignerApi();
    await loaded(api);

    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });
    const palette = within(screen.getByRole("dialog", { name: "Commands" }));
    fireEvent.change(palette.getByRole("combobox", { name: "Command" }), { target: { value: "template" } });
    fireEvent.keyDown(palette.getByRole("combobox", { name: "Command" }), { key: "Enter" });
    const dialog = within(screen.getByRole("dialog", { name: "Save as template" }));
    expect((dialog.getByRole("textbox", { name: "Template name" }) as HTMLInputElement).value).toBe("Customer satisfaction 2026");
    fireEvent.change(dialog.getByRole("textbox", { name: "Template name" }), { target: { value: "Acme check-in" } });
    fireEvent.click(dialog.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(api.templates).toEqual(["Acme check-in"]));
    expect(await screen.findByText("Saved as a template. New surveys can start from it.")).toBeTruthy();
  });
});
