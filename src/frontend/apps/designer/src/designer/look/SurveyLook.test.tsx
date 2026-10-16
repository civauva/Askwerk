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
const look = () => within(screen.getByRole("main", { name: "Look" }));
const areas = () => within(screen.getByRole("complementary", { name: "Look settings" }));
const saved = { timeout: 3000 };

async function openLook(api = new FakeDesignerApi()) {
  const view = render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} previewEmail={() => Promise.resolve("<p>Hello</p>")} />);
  await screen.findByRole("navigation", { name: "Outline" });
  fireEvent.click(tabs().getByRole("link", { name: "Look" }));
  await screen.findByRole("button", { name: "Use the look Werkstatt" });
  await act(async () => {});

  return { api, ...view };
}

async function pick(name: string) {
  fireEvent.click(look().getByRole("button", { name: `Use the look ${name}` }));
  await screen.findByRole("complementary", { name: "Look settings" });
}

function open(area: string, part?: string) {
  fireEvent.click(areas().getByRole("button", { name: new RegExp(`^${area}`) }));

  if (part !== undefined) {
    fireEvent.click(areas().getByRole("button", { name: new RegExp(`^${part}`) }));
  }
}

describe("Create → Look: the gallery of looks (W26 A)", () => {
  it("opens on seventeen looks drawn with the survey's first choice question, filtered by what they are for, and no runner", { timeout: 40_000 }, async () => {
    const { container } = await openLook();

    expect(look().getAllByRole("button", { name: /^Use the look / })).toHaveLength(17);
    expect(look().getAllByText("Which branch?").length).toBeGreaterThan(0);
    expect(screen.queryByTitle("Survey preview")).toBeNull();
    fireEvent.click(look().getByRole("button", { name: "Print & PDF" }));
    expect(look().getAllByRole("button", { name: /^Use the look / }).map((card) => card.getAttribute("aria-label"))).toEqual(["Use the look Papier"]);
    fireEvent.click(look().getByRole("button", { name: "All 17" }));
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
    fireEvent.click(areas().getByRole("button", { name: "← All looks" }));
    expect(look().getByRole("button", { name: "Use the look Konfetti" }).getAttribute("aria-current")).toBe("true");
  });

  it("takes one of the organisation's looks instead, which drops the survey's own", async () => {
    const { api } = await openLook();

    fireEvent.click(look().getByRole("button", { name: /^Our organisation's looks|^In use:/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Acme Event" }));

    await waitFor(() => expect(api.themeCalls).toEqual(["t2"]));
  });
});

describe("Create → Look: make it yours (W26 B)", () => {
  it("changes any part in its folded area, counts the changes, and goes back to the look's choices", async () => {
    const { api, container } = await openLook();
    await pick("Konfetti");

    open("Components", "Choices");
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

    fireEvent.click(within(screen.getByRole("region", { name: "Make it yours" })).getByRole("group", { name: "Mode" }).querySelector("button:nth-child(2)")!);

    await waitFor(() => expect(api.lookCalls.at(-1)?.tokens.mode).toBe("dark"), saved);
    expect(checkTheme(api.lookCalls.at(-1)!.tokens)).toEqual([]);
  });

  it("does not save a look that is hard to read, and says why", async () => {
    const { api } = await openLook();
    await pick("Klar");
    await waitFor(() => expect(api.lookCalls).toHaveLength(1), saved);

    open("Colours");
    fireEvent.change(areas().getByRole("textbox", { name: "Text" }), { target: { value: "#ffffff" } });

    expect(await screen.findByText(/text is hard to read on surface/)).toBeTruthy();
    expect(screen.getByText("Not saved: fix the checks first.")).toBeTruthy();
    expect(api.lookCalls).toHaveLength(1);
  });

  it("keeps the survey's layout and header picture in the draft", async () => {
    const { api } = await openLook();
    await pick("Atelier");

    open("Pages");
    fireEvent.click(areas().getByRole("button", { name: "One at a time" }));
    await waitFor(() => expect(api.saved.at(-1)?.layout).toBe("one-at-a-time"));
    open("Logo, header & background");
    fireEvent.change(areas().getByLabelText("Header picture file"), { target: { files: [new File([new Uint8Array([137, 80])], "shop.png", { type: "image/png" })] } });

    await waitFor(() => expect(api.saved.at(-1)?.headerImage?.asset).toBe("a1"));
  });

  it("saves the look for the organisation, or says who may", async () => {
    const api = new FakeDesignerApi();
    await openLook(api);
    await pick("Sonne");

    fireEvent.click(areas().getByRole("button", { name: "Save as our organisation's look" }));
    fireEvent.change(within(screen.getByRole("dialog")).getByRole("textbox", { name: "Name" }), { target: { value: "Acme Summer" } });
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Saved as Acme Summer for the whole organisation.")).toBeTruthy();

    api.saveThemeOutcome = new ApiError(403, "4bf92f35", "role-not-allowed");
    fireEvent.click(areas().getByRole("button", { name: "Save as our organisation's look" }));
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
