// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Designer } from "../Designer";
import { ShellView, type View } from "../../shell/AppShell";
import { FakeDesignerApi, sequentialIds } from "../testing";
import { changesBetween, darkColoursFor, setAt } from "./lookEditing";
import { checkTheme, themePresets } from "@askwerk/schema";

afterEach(cleanup);

const tabs = () => within(screen.getByRole("navigation", { name: "Survey" }));
const areas = () => within(screen.getByRole("complementary", { name: "Look settings" }));
const gallery = () => within(screen.getByRole("dialog", { name: "Choose from gallery" }));
const saved = { timeout: 3000 };

async function openLook(api = new FakeDesignerApi()) {
  const view = render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} previewEmail={() => Promise.resolve("<p>Hello</p>")} />);
  await screen.findByRole("navigation", { name: "Outline" });
  fireEvent.click(tabs().getByRole("link", { name: "Look" }));
  await screen.findByRole("complementary", { name: "Look settings" });
  await act(async () => {});

  return { api, ...view };
}

async function pick(name: string) {
  fireEvent.click(areas().getByRole("button", { name: "Choose from gallery" }));
  fireEvent.click(gallery().getByRole("button", { name: `Use the look ${name}` }));
  await waitFor(() => expect(screen.queryByRole("dialog", { name: "Choose from gallery" })).toBeNull());
}

/** Opens a tab of the settings, then a folded area and one of its parts. */
function open(tab: "Parts" | "Pages", area: string, part?: string) {
  fireEvent.click(areas().getByRole("tab", { name: tab }));
  fireEvent.click(areas().getByRole("button", { name: new RegExp(`^${area}`) }));

  if (part !== undefined) {
    fireEvent.click(areas().getByRole("button", { name: new RegExp(`^${part}`) }));
  }
}

describe("Create → Look: the editor first, the gallery on demand (W26, W33)", () => {
  it("opens on the editor with the survey's look beside the Preview, Style first", async () => {
    const { container } = await openLook();

    expect(areas().getByRole("tab", { name: "Style" }).getAttribute("aria-selected")).toBe("true");
    expect(areas().getByRole("button", { name: "Condensed" }).getAttribute("aria-pressed")).toBe("true");
    expect(within(screen.getByRole("complementary", { name: "Preview" })).getByTitle("Survey preview")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    await expectNoAxeViolations(container);
  });

  it("offers seventeen looks in the gallery, drawn with the survey's first choice question and filtered by purpose", { timeout: 40_000 }, async () => {
    const { container } = await openLook();

    fireEvent.click(areas().getByRole("button", { name: "Choose from gallery" }));

    expect(gallery().getAllByRole("button", { name: /^Use the look / })).toHaveLength(17);
    expect(gallery().getAllByText("Which branch?").length).toBeGreaterThan(0);
    fireEvent.click(gallery().getByRole("button", { name: "Print & PDF" }));
    expect(gallery().getAllByRole("button", { name: /^Use the look / }).map((card) => card.getAttribute("aria-label"))).toEqual(["Use the look Papier"]);
    fireEvent.click(gallery().getByRole("button", { name: "All 17" }));
    await expectNoAxeViolations(container);
  });

  it("uses a look as the survey's own and shows it in the one Preview, docked beside the settings", async () => {
    const { api } = await openLook();

    await pick("Konfetti");

    await waitFor(() => expect(api.lookCalls.at(-1)?.lookBase).toBe("preset:konfetti"), saved);
    expect(api.lookCalls.at(-1)?.tokens.decoration?.pattern).toBe("confetti");
    const preview = within(screen.getByRole("complementary", { name: "Preview" }));
    expect((preview.getByTitle("Survey preview") as HTMLIFrameElement).getAttribute("src")).toBe("/s/s1?preview");
    expect(preview.queryByRole("button", { name: "Close preview" })).toBeNull();
    fireEvent.click(areas().getByRole("button", { name: "Choose from gallery" }));
    expect(gallery().getByRole("button", { name: "Use the look Konfetti" }).getAttribute("aria-current")).toBe("true");
  });

  it("takes one of the organisation's looks instead, which drops the survey's own", async () => {
    const { api } = await openLook();

    fireEvent.click(areas().getByRole("button", { name: "Choose from gallery" }));
    fireEvent.click(gallery().getByRole("button", { name: /^Our organisation's looks|^In use:/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Acme Event" }));

    await waitFor(() => expect(api.themeCalls).toEqual(["t2"]));
  });
});

describe("Create → Look: make it yours (W26 B)", () => {
  it("changes any part in its folded area, counts the changes, and goes back to the look's choices", async () => {
    const { api, container } = await openLook();
    await pick("Konfetti");

    open("Parts", "Components", "Choices");
    fireEvent.click(areas().getByRole("group", { name: "Marker" }).querySelector("button:nth-child(2)")!);

    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.components?.choices?.marker).toBe("check"), saved);
    expect(areas().getByText("changed 1 thing · for this survey")).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(areas().getByRole("button", { name: "Back to Konfetti's choices" }));
    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.components?.choices?.marker).toBeUndefined(), saved);
  });

  it("switches to dark mode with dark colours that pass the checks", async () => {
    const { api } = await openLook();
    await pick("Werkstatt");

    open("Parts", "Colours");
    fireEvent.click(areas().getByRole("group", { name: "Mode" }).querySelector("button:nth-child(2)")!);

    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.mode).toBe("dark"), saved);
    expect(checkTheme(api.lookCalls.at(-1)!.tokens)).toEqual([]);
  });

  it("does not save a look that is hard to read, and says why", async () => {
    const { api } = await openLook();
    await pick("Klar");
    await waitFor(() => expect(api.lookCalls).toHaveLength(1), saved);

    open("Parts", "Colours");
    fireEvent.change(areas().getByRole("textbox", { name: "Text" }), { target: { value: "#ffffff" } });

    expect(await screen.findByText(/text is hard to read on surface/)).toBeTruthy();
    expect(screen.getByText("Not saved: fix the checks first.")).toBeTruthy();
    expect(api.lookCalls).toHaveLength(1);
  });

  it("keeps the survey's layout and header picture in the draft", async () => {
    const { api } = await openLook();
    await pick("Atelier");

    open("Pages", "Pages");
    fireEvent.click(areas().getByRole("button", { name: "One at a time" }));
    await waitFor(() => expect(api.saved.at(-1)?.layout).toBe("one-at-a-time"));
    open("Pages", "Logo, header & background");
    fireEvent.change(areas().getByLabelText("Header picture file"), { target: { files: [new File([new Uint8Array([137, 80])], "shop.png", { type: "image/png" })] } });

    await waitFor(() => expect(api.saved.at(-1)?.headerImage?.asset).toBe("a1"));
  });

  it("saves the look for the organisation, or says who may", async () => {
    const api = new FakeDesignerApi();
    await openLook(api);
    await pick("Sonne");

    fireEvent.click(areas().getByRole("button", { name: "More actions" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Save as our organisation's look" }));
    fireEvent.change(within(screen.getByRole("dialog")).getByRole("textbox", { name: "Name" }), { target: { value: "Acme Summer" } });
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Saved as Acme Summer for the whole organisation.")).toBeTruthy();

    api.saveThemeOutcome = new ApiError(403, "4bf92f35", "role-not-allowed");
    fireEvent.click(areas().getByRole("button", { name: "More actions" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Save as our organisation's look" }));
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Only owners and admins save looks for the organisation.")).toBeTruthy();
    expect(api.savedThemes).toEqual(["Acme Summer"]);
  });

  it("shows the invitation email and the PDF in the same Preview", async () => {
    await openLook();
    await pick("Papier");
    const preview = within(screen.getByRole("complementary", { name: "Preview" }));

    fireEvent.click(preview.getByRole("button", { name: "PDF" }));
    expect((preview.getByTitle("Survey as PDF") as HTMLIFrameElement).getAttribute("src")).toBe("/api/app/surveys/s1/pdf?language=en");
    fireEvent.click(preview.getByRole("button", { name: "Email" }));
    await waitFor(() => expect((preview.getByTitle("Invitation email") as HTMLIFrameElement).getAttribute("srcdoc")).toBe("<p>Hello</p>"));
  });
});

describe("Create → Look: Style as tiles, the Preview on its stage (W32, W33)", () => {
  it("shows Style as in the mockup: accent colour, three faces, shape, background, density and readability last", async () => {
    const { container } = await openLook();

    expect([...container.querySelectorAll(".sl-tab .sl-lbl")].map((label) => label.textContent)).toEqual(["Accent colour", "Type", "Shape", "Background", "Density"]);
    expect(areas().getByRole("group", { name: "Density" })).toBeTruthy();
    expect(container.querySelector(".sl-tab")!.lastElementChild!.previousElementSibling!.className).toBe("sl-readable");
    expect(within(areas().getByRole("group", { name: "Type" })).getAllByRole("button").map((tile) => tile.textContent)).toEqual(["AaSans", "AaCondensed", "AaSerif"]);
    expect(within(areas().getByRole("group", { name: "Background" })).getAllByRole("button").map((tile) => tile.textContent)).toEqual(["Colour", "Pattern", "Picture"]);
    expect(areas().getByText(/^Readable everywhere · text \d+\.\d : 1 · buttons \d+\.\d : 1$/)).toBeTruthy();
    expect(areas().getByText("for this survey")).toBeTruthy();
    expect(areas().queryByText("All colours")).toBeNull();
    expect(areas().queryByRole("group", { name: "Mode" })).toBeNull();
  });

  it("changes accent, type and background from the Style tab, a picture uploaded on the spot", async () => {
    const { api } = await openLook();

    fireEvent.click(areas().getByRole("button", { name: "#1f5f8b" }));
    fireEvent.click(areas().getByRole("button", { name: "Serif" }));
    fireEvent.click(areas().getByRole("button", { name: "Pattern" }));
    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.decoration?.pattern).toBe("confetti"), saved);
    fireEvent.change(areas().getByLabelText("Background picture file"), { target: { files: [new File([new Uint8Array([137, 80])], "hall.png", { type: "image/png" })] } });

    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.background?.image?.asset).toBe("a1"), saved);
    expect(api.lookCalls.at(-1)?.tokens.decoration).toBeUndefined();
    expect(api.lookCalls.at(-1)?.tokens.colours.accent).toBe("#1f5f8b");
    expect(api.lookCalls.at(-1)?.tokens.fonts.heading.family).toBe("IBM Plex Serif");
    expect(areas().getByRole("button", { name: "Picture" }).getAttribute("aria-pressed")).toBe("true");
  });

  it("zooms the page out and back, also with Ctrl + scroll, and shows a tablet width", async () => {
    await openLook();
    const preview = within(screen.getByRole("complementary", { name: "Preview" }));
    const page = () => (preview.getByTitle("Survey preview") as HTMLIFrameElement).parentElement!;

    fireEvent.click(preview.getByRole("button", { name: "Zoom out" }));
    expect(preview.getByText("90 %")).toBeTruthy();
    expect(page().style.transform).toBe("scale(0.9)");
    fireEvent.click(preview.getByRole("button", { name: "Fit" }));
    expect(page().style.transform).toBe("");
    const wheel = new WheelEvent("wheel", { deltaY: -100, ctrlKey: true, cancelable: true, bubbles: true });
    preview.getByRole("region", { name: "Zoom" }).dispatchEvent(wheel);
    expect(wheel.defaultPrevented).toBe(true);
    expect(await preview.findByText("110 %")).toBeTruthy();
    fireEvent.click(preview.getByRole("button", { name: "Tablet" }));
    expect(page().style.width).toBe("768px");
    expect(preview.getByRole("button", { name: "Tablet" }).getAttribute("aria-pressed")).toBe("true");
  });

  it("says in the stage bar where the survey is, and jumps to the welcome and thank-you pages", async () => {
    await openLook();
    const preview = within(screen.getByRole("complementary", { name: "Preview" }));
    const frame = preview.getByTitle("Survey preview") as HTMLIFrameElement;
    const sent: unknown[] = [];
    frame.contentWindow!.addEventListener("message", (event) => sent.push(event.data));

    await act(async () => {
      window.dispatchEvent(new MessageEvent("message", { data: { type: "askwerk:preview-page", at: 2, pages: 4 }, origin: window.location.origin, source: frame.contentWindow }));
    });
    expect(preview.getByText(/^Page 2 of 4 ·/)).toBeTruthy();
    fireEvent.click(preview.getByRole("button", { name: "Thank you" }));
    fireEvent.click(preview.getByRole("button", { name: "Welcome" }));

    await waitFor(() => expect(sent).toContainEqual({ type: "askwerk:goto", target: "end" }));
    expect(sent).toContainEqual({ type: "askwerk:goto", target: "welcome" });
    expect(preview.getByRole("button", { name: "Open in new tab" })).toBeTruthy();
  });
});

describe("Create → Look: click a part of the preview to edit it (W44 A, AWK-69)", () => {
  it("turns the preview into a map of its parts and opens the settings of the part clicked there", async () => {
    await openLook();
    const preview = within(screen.getByRole("complementary", { name: "Preview" }));
    const frame = preview.getByTitle("Survey preview") as HTMLIFrameElement;
    const sent: unknown[] = [];
    frame.contentWindow!.addEventListener("message", (event) => sent.push(event.data));
    const from = (data: object) => window.dispatchEvent(new MessageEvent("message", { data, origin: window.location.origin, source: frame.contentWindow }));

    await act(async () => from({ type: "askwerk:preview-ready" }));
    await waitFor(() => expect(sent.some((message) => (message as { type?: string }).type === "askwerk:pick-mode")).toBe(true));
    const mode = sent.find((message) => (message as { type?: string }).type === "askwerk:pick-mode") as { labels: Record<string, string> };
    expect(mode.labels["components/choices"]).toBe("Choices · click to edit");

    await act(async () => from({ type: "askwerk:pick", area: "components", part: "choices" }));
    await waitFor(() => expect(areas().getByRole("tab", { name: "Parts" }).getAttribute("aria-selected")).toBe("true"));
    expect(areas().getByRole("button", { name: /^Choices/ }).getAttribute("aria-expanded")).toBe("true");

    await act(async () => from({ type: "askwerk:pick", area: "header" }));
    await waitFor(() => expect(areas().getByRole("tab", { name: "Pages" }).getAttribute("aria-selected")).toBe("true"));

    // Only the preview beside the settings may pick.
    await act(async () => window.dispatchEvent(new MessageEvent("message", { data: { type: "askwerk:pick", area: "components", part: "buttons" }, origin: "https://elsewhere.example", source: frame.contentWindow })));
    expect(areas().getByRole("tab", { name: "Pages" }).getAttribute("aria-selected")).toBe("true");
  });
});

describe("lookEditing", () => {
  it("sets and removes a setting by its path, and counts what differs", () => {
    const werkstatt = themePresets[0]!.tokens;
    const changed = setAt(werkstatt, ["components", "choices", "columns"], "two");

    expect(changed.components?.choices?.columns).toBe("two");
    expect(changesBetween(werkstatt, changed)).toBe(1);
    expect(setAt(changed, ["components", "choices", "columns"], undefined).components).toBeUndefined();
  });

  it("makes dark colours that pass the checks for every look", () => {
    for (const preset of themePresets) {
      expect(checkTheme({ ...preset.tokens, mode: "dark", darkColours: darkColoursFor(preset.tokens.colours) })).toEqual([]);
    }
  });
});

describe("Create → Look in the Simple view (W45, AWK-71)", () => {
  it("shows only Style, with the way to the rest, and leaves the preview a plain preview", async () => {
    const setView = vi.fn<(view: View) => void>();
    render(
      <ShellView.Provider value={{ view: "simple", setView }}>
        <Designer api={new FakeDesignerApi()} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} previewEmail={() => Promise.resolve("<p>Hello</p>")} />
      </ShellView.Provider>,
    );
    await screen.findByRole("navigation", { name: "Outline" });
    fireEvent.click(tabs().getByRole("link", { name: "Look" }));
    await screen.findByRole("complementary", { name: "Look settings" });

    expect(areas().getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Style"]);
    const frame = within(screen.getByRole("complementary", { name: "Preview" })).getByTitle("Survey preview") as HTMLIFrameElement;
    const sent: unknown[] = [];
    frame.contentWindow!.addEventListener("message", (event) => sent.push(event.data));
    await act(async () => window.dispatchEvent(new MessageEvent("message", { data: { type: "askwerk:preview-ready" }, origin: window.location.origin, source: frame.contentWindow })));
    await waitFor(() => expect(sent.some((message) => (message as { type?: string }).type === "askwerk:look")).toBe(true));
    expect(sent.some((message) => (message as { type?: string }).type === "askwerk:pick-mode")).toBe(false);

    fireEvent.click(areas().getByRole("button", { name: "Switch to Advanced" }));
    expect(setView).toHaveBeenCalledWith("advanced");
  });
});
