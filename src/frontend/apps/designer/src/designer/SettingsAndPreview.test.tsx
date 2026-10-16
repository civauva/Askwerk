// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Designer } from "./Designer";
import { FakeDesignerApi, sampleDraft, sequentialIds } from "./testing";

afterEach(cleanup);

async function loaded(api = new FakeDesignerApi(), openWindow: (url: string) => void = () => undefined) {
  const view = render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} openWindow={openWindow} />);
  await screen.findByRole("navigation", { name: "Outline" });
  await act(async () => {});

  return { api, ...view };
}

const outline = () => within(screen.getByRole("navigation", { name: "Outline" }));
const canvas = () => within(screen.getByRole("main", { name: "Canvas" }));
const bar = () => within(screen.getByRole("banner"));
const tabs = () => within(screen.getByRole("navigation", { name: "Survey" }));

describe("Create → Look (W23, W26)", () => {
  it("is its own tab, opens from the command palette, and Build takes the questions back", async () => {
    await loaded();

    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });
    const palette = within(screen.getByRole("dialog", { name: "Commands" }));
    fireEvent.change(palette.getByRole("combobox", { name: "Command" }), { target: { value: "look" } });
    fireEvent.keyDown(palette.getByRole("combobox", { name: "Command" }), { key: "Enter" });
    expect(await screen.findByRole("main", { name: "Look" })).toBeTruthy();
    expect(tabs().getByRole("link", { name: "Look" }).getAttribute("aria-current")).toBe("page");
    fireEvent.click(tabs().getByRole("link", { name: "Questions" }));

    expect(screen.queryByRole("main", { name: "Look" })).toBeNull();
    expect(outline().getByRole("button", { name: /Age group/ })).toBeTruthy();
  });
});

describe("Welcome and thank-you pages in the page list (W23; were in the settings, W9 A)", () => {
  it("edits the welcome page from the top of the page list and switches it off", async () => {
    const { api, container } = await loaded();

    fireEvent.click(outline().getByRole("button", { name: /^Welcome page/ }));
    expect(canvas().getByRole("textbox", { name: "Welcome title" })).toBeTruthy();
    const panel = within(screen.getByRole("complementary", { name: "Welcome page" }));
    expect(panel.getByRole("switch", { name: "Show a welcome page" }).getAttribute("aria-checked")).toBe("true");
    await expectNoAxeViolations(container);
    fireEvent.click(panel.getByRole("switch", { name: "Show a welcome page" }));

    await waitFor(() => expect(api.saved.at(-1)?.welcome?.enabled).toBe(false));
    expect(canvas().getByText("Off · respondents start with the first question.")).toBeTruthy();
    expect(outline().getByRole("button", { name: /^Welcome page/ }).textContent).toContain("off");
  });

  it("edits the texts in the editing language, and an empty thank-you text leaves the standard text", async () => {
    const { api } = await loaded();

    fireEvent.click(outline().getByRole("button", { name: /^Welcome page/ }));
    fireEvent.change(canvas().getByRole("textbox", { name: "Welcome title" }), { target: { value: "Customer satisfaction 2026" } });
    fireEvent.click(bar().getByRole("button", { name: "DE" }));
    fireEvent.change(canvas().getByRole("textbox", { name: "Welcome title" }), { target: { value: "Kundenzufriedenheit 2026" } });
    fireEvent.click(bar().getByRole("button", { name: "EN" }));
    fireEvent.click(outline().getByRole("button", { name: "Thank-you page" }));
    expect((canvas().getByRole("textbox", { name: "Thank-you text" }) as HTMLInputElement).value).toBe("Thank you.");
    fireEvent.change(canvas().getByRole("textbox", { name: "Thank-you text" }), { target: { value: "" } });

    await waitFor(() => expect(api.saved.at(-1)?.welcome?.title).toEqual({ en: "Customer satisfaction 2026", de: "Kundenzufriedenheit 2026" }));
    await waitFor(() => expect(api.saved.at(-1)?.endPage).toBeUndefined());
    expect(canvas().getByRole("textbox", { name: "Thank-you text" }).getAttribute("placeholder")).toBe("Thank you! Your answers are saved.");
  });
});

describe("Preview (AWK-22, W9 B and C, M5 B)", () => {
  const panel = () => within(screen.getByRole("complementary", { name: "Preview" }));
  const frame = () => panel().getByTitle("Survey preview") as HTMLIFrameElement;

  function choosePreview(item: "In a side panel" | "In a new tab" | "As PDF") {
    fireEvent.click(bar().getByRole("button", { name: "Preview" }));
    fireEvent.click(screen.getByRole("menuitem", { name: item }));
  }

  it("shows the last saved draft in a side panel at desktop or phone width, restarts and closes", async () => {
    await loaded();

    choosePreview("In a side panel");

    expect(panel().getByText("Preview · answers are not saved")).toBeTruthy();
    expect(frame().getAttribute("src")).toBe("/s/s1?preview");
    expect(panel().getByRole("button", { name: "Desktop" }).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(panel().getByRole("button", { name: "Phone" }));
    expect(frame().style.width).toBe("390px");
    const before = frame();
    fireEvent.click(panel().getByRole("button", { name: "Restart" }));
    expect(frame()).not.toBe(before);
    fireEvent.click(panel().getByRole("button", { name: "Close preview" }));
    expect(screen.queryByRole("complementary", { name: "Preview" })).toBeNull();
  });

  it("opens the draft in a new tab", async () => {
    const openWindow = vi.fn();
    await loaded(new FakeDesignerApi(), openWindow);

    choosePreview("In a new tab");

    expect(openWindow).toHaveBeenCalledWith("/s/s1?preview");
    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("opens the survey as a PDF to print, in the language being edited (AWK-17 step 7)", async () => {
    const openWindow = vi.fn();
    await loaded(new FakeDesignerApi(), openWindow);
    fireEvent.click(bar().getByRole("button", { name: "DE" }));

    choosePreview("As PDF");

    expect(openWindow).toHaveBeenCalledWith("/api/app/surveys/s1/pdf?language=de");
  });

  it("switches to another part of the survey with the tabs, Survey settings included (W17, W23, W25)", async () => {
    const onTab = vi.fn();
    render(<Designer api={new FakeDesignerApi()} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} onTab={onTab} />);
    await screen.findByRole("navigation", { name: "Outline" });

    expect(tabs().getByRole("link", { name: "Questions" }).getAttribute("aria-current")).toBe("page");
    fireEvent.click(tabs().getByRole("link", { name: /^Results/ }));
    fireEvent.click(tabs().getByRole("link", { name: "Survey settings" }));

    expect(onTab).toHaveBeenCalledWith("results");
    expect(onTab).toHaveBeenCalledWith("survey-settings");
  });

  it("shares a published survey without changes from the bar, and publishes changes once there are some (W17)", async () => {
    const onTab = vi.fn();
    const api = new FakeDesignerApi({ ...sampleDraft, published: sampleDraft.definition });
    render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} onTab={onTab} />);
    await screen.findByRole("navigation", { name: "Outline" });

    fireEvent.click(await bar().findByRole("button", { name: "Share" }));
    expect(onTab).toHaveBeenCalledWith("share");
    expect(bar().getByText("Published · v3")).toBeTruthy();
    fireEvent.click(outline().getByRole("button", { name: /Age group/ }));
    fireEvent.change(screen.getByRole("textbox", { name: "Question text" }), { target: { value: "Your age group" } });

    expect(await bar().findByRole("button", { name: "Publish changes" })).toBeTruthy();
    expect(await bar().findByText("Draft · changes since v3")).toBeTruthy();
  });

  it("renames the survey by clicking its name (W23)", async () => {
    const { api } = await loaded();

    fireEvent.click(bar().getByRole("button", { name: "Customer satisfaction 2026, click to rename" }));
    const field = screen.getByRole("textbox", { name: "Title (internal, not shown to respondents)" });
    fireEvent.change(field, { target: { value: "Guest survey" } });
    fireEvent.keyDown(field, { key: "Enter" });

    expect(await bar().findByRole("button", { name: "Guest survey, click to rename" })).toBeTruthy();
    expect(api.renamed).toEqual(["Guest survey"]);
  });

  it("toggles the side panel with Ctrl Shift P, and the menu closes with Escape", async () => {
    await loaded();

    fireEvent.keyDown(document.body, { key: "P", ctrlKey: true, shiftKey: true });
    expect(screen.getByRole("complementary", { name: "Preview" })).toBeTruthy();
    fireEvent.keyDown(document.body, { key: "P", ctrlKey: true, shiftKey: true });
    expect(screen.queryByRole("complementary", { name: "Preview" })).toBeNull();
    fireEvent.click(bar().getByRole("button", { name: "Preview" }));
    fireEvent.keyDown(document.body, { key: "Escape" });

    expect(screen.queryByRole("menu")).toBeNull();
  });
});

describe("Approvals in the editor (AWK-28, W16)", () => {
  it("approves from the Preview panel; the bar and the Publish dialog say so", async () => {
    const api = new FakeDesignerApi();
    await loaded(api);
    expect(bar().getByRole("button", { name: "Not approved" })).toBeTruthy();

    fireEvent.keyDown(document.body, { key: "P", ctrlKey: true, shiftKey: true });
    fireEvent.click(within(screen.getByRole("complementary", { name: "Preview" })).getByRole("button", { name: "Approve…" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Approve this draft" })).getByRole("button", { name: "Approve" }));

    expect(await bar().findByRole("button", { name: /^✓ Approved · Anna Berger, / })).toBeTruthy();
    expect(within(screen.getByRole("complementary", { name: "Preview" })).getByText(/^✓ Approved by you · /)).toBeTruthy();
    expect(api.approved).toEqual([null]);
    fireEvent.click(bar().getByRole("button", { name: "Publish" }));
    expect(within(screen.getByRole("dialog", { name: /^Publish/ })).getByText("✓ Approved by Anna Berger for this draft.")).toBeTruthy();
  });

  it("asks again after a change: the approval is for the earlier draft", async () => {
    const api = new FakeDesignerApi();
    await loaded(api);
    fireEvent.keyDown(document.body, { key: "P", ctrlKey: true, shiftKey: true });
    fireEvent.click(screen.getByRole("button", { name: "Approve…" }));
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));
    await bar().findByRole("button", { name: /^✓ Approved/ });

    fireEvent.click(outline().getByRole("button", { name: /Age group/ }));
    fireEvent.change(screen.getByRole("textbox", { name: "Question text" }), { target: { value: "Your age group" } });

    expect(await bar().findByRole("button", { name: "! Approved an earlier draft" })).toBeTruthy();
  });
});
