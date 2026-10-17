// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Designer } from "./Designer";
import { FakeDesignerApi, sampleDraft, sequentialIds } from "./testing";
import { dropLine, grab, moveTo, placeAt, release } from "./dragTesting";

afterEach(cleanup);

async function loaded(api = new FakeDesignerApi()) {
  const view = render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} />);
  await screen.findByRole("navigation", { name: "Outline" });
  await act(async () => {});

  return { api, ...view };
}

const outline = () => within(screen.getByRole("navigation", { name: "Outline" }));
const pageTitles = () => outline().getAllByRole("heading", { level: 2 }).map((heading) => heading.textContent?.replace(/\d+$/, "").trim());
/** Selects a page; its Duplicate and Delete sit beside its title on the canvas (W23). */
const selectPage = (name: string) => fireEvent.click(outline().getByRole("button", { name }));

describe("Pages in the designer (AWK-29, W17 A)", () => {
  it("selects a page from the outline; its title is edited on the canvas and the inspector shows the page", async () => {
    const { api, container } = await loaded();

    fireEvent.click(outline().getByRole("button", { name: "02 · Your visit" }));
    const title = screen.getByRole("textbox", { name: "Page title" }) as HTMLInputElement;
    expect(title.value).toBe("Your visit");
    const page = within(screen.getByRole("complementary", { name: "Page settings" }));
    expect(page.getByText("Page 2 of 3")).toBeTruthy();
    expect(page.getByText("3 questions on this page")).toBeTruthy();
    fireEvent.change(title, { target: { value: "Your last visit" } });

    expect(outline().getByRole("button", { name: "02 · Your last visit" })).toBeTruthy();
    await waitFor(() => expect(api.saved.at(-1)?.pages[1]?.title).toEqual({ de: "Ihr Besuch", en: "Your last visit" }));
    await expectNoAxeViolations(container);
  });

  it("selects a page by clicking its header on the canvas", async () => {
    await loaded();

    fireEvent.click(screen.getByRole("button", { name: "Page 3 of 3 · About you" }));

    expect((screen.getByRole("textbox", { name: "Page title" }) as HTMLInputElement).value).toBe("About you");
  });

  it("moves a page down and up with Alt+arrows, keeping the focus on it (W23)", async () => {
    await loaded();

    fireEvent.keyDown(outline().getByRole("button", { name: "01 · Welcome" }), { key: "ArrowDown", altKey: true });
    expect(pageTitles()).toEqual(["01 · Your visit", "02 · Welcome", "03 · About you"]);
    expect(document.activeElement?.textContent).toBe("02 · Welcome");
    fireEvent.keyDown(outline().getByRole("button", { name: "02 · Welcome" }), { key: "ArrowUp", altKey: true });

    expect(pageTitles()).toEqual(["01 · Welcome", "02 · Your visit", "03 · About you"]);
  });

  it("duplicates a page after it and selects the copy", async () => {
    await loaded();

    selectPage("03 · About you");
    fireEvent.click(screen.getByRole("button", { name: "Duplicate" }));

    expect(pageTitles()).toEqual(["01 · Welcome", "02 · Your visit", "03 · About you", "04 · About you"]);
    expect(within(screen.getByRole("complementary", { name: "Page settings" })).getByText("Page 4 of 4")).toBeTruthy();
  });

  it("deletes a page after asking, with the number of its questions", async () => {
    await loaded();

    selectPage("02 · Your visit");
    fireEvent.click(screen.getByRole("button", { name: "Delete page…" }));
    const ask = within(screen.getByRole("dialog", { name: "Delete page" }));
    expect(ask.getByText("Delete “Your visit” and its 3 questions?")).toBeTruthy();
    fireEvent.click(ask.getByRole("button", { name: "Delete" }));

    expect(pageTitles()).toEqual(["01 · Welcome", "02 · About you"]);
  });

  it("offers no Delete for the only page", async () => {
    await loaded(new FakeDesignerApi({ ...sampleDraft, definition: { ...sampleDraft.definition, pages: [sampleDraft.definition.pages[0]!] } }));

    selectPage("01 · Welcome");

    expect(screen.queryByRole("button", { name: "Delete page…" })).toBeNull();
    expect(screen.getByRole("button", { name: "Duplicate" })).toBeTruthy();
  });

  it("moves a page by dragging its grip in the page list, marked before the page it lands on (W34)", async () => {
    await loaded();
    const page = outline().getByRole("button", { name: "03 · About you" });
    const welcome = outline().getByRole("button", { name: "01 · Welcome" }).closest("section")!;
    placeAt(welcome, 100, 100);

    grab(page.querySelector("[data-grip]")!);
    moveTo(120);
    expect(page.getAttribute("data-lifted")).toBe("true");
    expect(document.querySelector(".designer-lift")?.textContent).toBe("03 · About you");
    expect(welcome.firstElementChild).toBe(dropLine());
    release();

    expect(pageTitles()).toEqual(["01 · About you", "02 · Welcome", "03 · Your visit"]);
    expect(screen.getByText("Moved to position 1 of 3.")).toBeTruthy();
  });

  it("moves a page after another one when dropped on its lower half", async () => {
    await loaded();
    const welcome = outline().getByRole("button", { name: "01 · Welcome" });
    const visit = outline().getByRole("button", { name: "02 · Your visit" }).closest("section")!;
    placeAt(visit, 100, 100);

    grab(welcome);
    moveTo(180);
    expect(visit.lastElementChild).toBe(dropLine());
    release();

    expect(pageTitles()).toEqual(["01 · Your visit", "02 · Welcome", "03 · About you"]);
  });

  it("moves a page on the canvas by the grip in its header (W34)", async () => {
    await loaded();
    const canvas = within(screen.getByRole("main", { name: "Canvas" }));
    const first = canvas.getByRole("button", { name: "Move page 1" }).closest("[data-drop-kind='page']")!;
    placeAt(first, 0, 200);

    grab(canvas.getByRole("button", { name: "Move page 3" }));
    moveTo(20);
    release();

    expect(pageTitles()).toEqual(["01 · About you", "02 · Welcome", "03 · Your visit"]);
  });
});
