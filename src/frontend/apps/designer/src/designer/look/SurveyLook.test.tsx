// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Designer } from "../Designer";
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
    expect(areas().getByText(/changed 1 thing/)).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(areas().getByRole("button", { name: "Back to Konfetti's choices" }));
    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.components?.choices?.marker).toBeUndefined(), saved);
  });

  it("switches to dark mode with dark colours that pass the checks", async () => {
    const { api } = await openLook();
    await pick("Werkstatt");

    fireEvent.click(areas().getByRole("group", { name: "Mode" }).querySelector("button:nth-child(2)")!);

    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.mode).toBe("dark"), saved);
    expect(checkTheme(api.lookCalls.at(-1)!.tokens)).toEqual([]);
  });

  it("does not save a look that is hard to read, and says why", async () => {
    const { api } = await openLook();
    await pick("Klar");
    await waitFor(() => expect(api.lookCalls).toHaveLength(1), saved);

    fireEvent.click(areas().getByText("All colours"));
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

describe("Create → Look: Style as tiles, the Preview zooms (W33)", () => {
  it("changes accent, type and background from the Style tab", async () => {
    const { api } = await openLook();

    fireEvent.click(areas().getByRole("button", { name: "#1f5f8b" }));
    fireEvent.click(areas().getByRole("button", { name: "Serif" }));
    fireEvent.click(areas().getByRole("button", { name: "Mountains" }));

    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.decoration?.pattern).toBe("mountains"), saved);
    expect(api.lookCalls.at(-1)?.tokens.colours.accent).toBe("#1f5f8b");
    expect(api.lookCalls.at(-1)?.tokens.fonts.heading.family).toBe("IBM Plex Serif");
  });

  it("zooms the preview out and back, and shows a tablet width", async () => {
    await openLook();
    const preview = within(screen.getByRole("complementary", { name: "Preview" }));
    const frame = () => preview.getByTitle("Survey preview") as HTMLIFrameElement;

    fireEvent.click(preview.getByRole("button", { name: "Zoom out" }));
    expect(preview.getByText("90 %")).toBeTruthy();
    expect(frame().style.transform).toBe("scale(0.9)");
    fireEvent.click(preview.getByRole("button", { name: "Fit" }));
    expect(frame().style.transform).toBe("");
    fireEvent.click(preview.getByRole("button", { name: "Tablet" }));
    expect(frame().style.width).toBe("768px");
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
