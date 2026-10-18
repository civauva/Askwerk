// SPDX-License-Identifier: AGPL-3.0-only
import { themePresets } from "@askwerk/schema";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { LookPage } from "./LookPage";
import { FakeThemesClient } from "./testing";

afterEach(cleanup);

async function shown(client = new FakeThemesClient(), locale: "de" | "en" = "en") {
  const view = render(<LookPage client={client} locale={locale} saveDelayMs={0} />);
  await screen.findByRole("heading", { level: 1 });
  await act(async () => {});

  return { client, ...view };
}

const list = () => within(screen.getByRole("navigation", { name: "Themes" }));
const editor = () => within(screen.getByRole("region", { name: "Theme" }));

describe("Administration → Looks (AWK-17, W10 A, W32): stupid simple", () => {
  it("starts an organisation without themes from a preset, which becomes the default", async () => {
    const { client } = await shown(new FakeThemesClient([]));

    expect(screen.getByText("No theme yet. Your surveys use Werkstatt.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Start from Klar" }));

    await waitFor(() => expect(client.calls).toContainEqual(["create", "Klar", themePresets[1]!.tokens]));
    // The list appears once the new theme is stored; wait for it before looking inside.
    await screen.findByRole("navigation", { name: "Themes" });
    expect(await list().findByRole("button", { name: "Klar Default" })).toBeTruthy();
    expect((editor().getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Klar");
  });

  it("lists the themes with the default first and opens the first", async () => {
    await shown();

    expect(list().getAllByRole("button").map((button) => button.textContent)).toEqual(["Acme Standard Default", "Acme Event", "New theme"]);
    expect(list().getByRole("button", { name: "Acme Standard Default" }).getAttribute("aria-current")).toBe("true");
    expect((editor().getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Acme Standard");
  });

  it("shows only name, logo and accent colour, and the rest under More", async () => {
    await shown();

    expect(editor().getByRole("textbox", { name: "Name" })).toBeTruthy();
    expect(editor().getByRole("button", { name: "Upload logo…" })).toBeTruthy();
    expect(editor().getByLabelText("Accent colour")).toBeTruthy();
    expect(editor().queryByRole("group", { name: "Corners" })).toBeNull();
    fireEvent.click(editor().getByRole("button", { name: "More" }));
    expect(editor().getByRole("group", { name: "Corners" })).toBeTruthy();
    expect(editor().getByRole("group", { name: "Layout" })).toBeTruthy();
  });

  it("saves changes by itself, and picks readable button text for a new accent colour", async () => {
    const { client } = await shown();

    fireEvent.change(editor().getByRole("textbox", { name: "Name" }), { target: { value: "Acme 2026" } });
    fireEvent.change(editor().getByRole("textbox", { name: "Accent colour (hex)" }), { target: { value: "#0b6e4f" } });

    await waitFor(() => expect(client.lastUpdate("t1")?.name).toBe("Acme 2026"));
    await waitFor(() => expect(client.lastUpdate("t1")?.tokens.colours).toMatchObject({ accent: "#0b6e4f", onAccent: "#ffffff" }));
    fireEvent.change(editor().getByRole("textbox", { name: "Accent colour (hex)" }), { target: { value: "#c77800" } });
    await waitFor(() => expect(client.lastUpdate("t1")?.tokens.colours).toMatchObject({ accent: "#c77800", onAccent: "#1a1917" }));
    expect(editor().getByText("Saved")).toBeTruthy();
  });

  it("says in plain words which colours are too weak, and does not save until they are fixed", async () => {
    const { client } = await shown();
    fireEvent.click(editor().getByRole("button", { name: "More" }));
    const before = client.calls.filter(([name]) => name === "update").length;

    fireEvent.change(editor().getByRole("textbox", { name: "Text (hex)" }), { target: { value: "#9a948c" } });

    const alert = await editor().findByRole("alert");
    expect(alert.textContent).toContain("Text on cards: 3 : 1, needs at least 4.5 : 1.");
    expect(alert.textContent).toContain("Not saved until this is fixed.");
    expect(client.calls.filter(([name]) => name === "update").length).toBe(before);
    fireEvent.change(editor().getByRole("textbox", { name: "Text (hex)" }), { target: { value: "#1a1917" } });
    await waitFor(() => expect(editor().queryByRole("alert")).toBeNull());
  });

  it("uploads a logo and shows it in the preview", async () => {
    const { client } = await shown();

    fireEvent.change(editor().getByLabelText("Logo file"), { target: { files: [new File([new Uint8Array([137, 80])], "logo.png", { type: "image/png" })] } });

    await waitFor(() => expect(client.calls).toContainEqual(["upload", "logo.png", "image"]));
    await waitFor(() => expect(client.lastUpdate("t1")?.tokens.logo?.asset).toBeDefined());
    const preview = within(screen.getByRole("region", { name: "Preview" }));
    expect(preview.getByRole("img", { name: "Logo" }).getAttribute("src")).toMatch(/^\/api\/app\/assets\/a\d+$/);
  });

  it("opens the field of a part clicked in the preview, under More when it lives there (W44 A, AWK-69)", async () => {
    await shown();
    const preview = screen.getByRole("region", { name: "Preview" });
    const card = preview.querySelector<HTMLElement>("[data-pick='surface']")!;

    expect(card.getAttribute("data-pick-label")).toBe("Cards · click to edit");
    expect(editor().queryByRole("group", { name: "Colours" })).toBeNull();
    fireEvent.click(card);

    expect(editor().getByRole("button", { name: "Less" }).getAttribute("aria-expanded")).toBe("true");
    await waitFor(() => expect(document.querySelector("[data-field='surface']")?.classList.contains("look-picked")).toBe(true));
    fireEvent.click(preview.querySelector<HTMLElement>("[data-pick='logo']")!);
    await waitFor(() => expect(document.querySelector("[data-field='logo']")?.classList.contains("look-picked")).toBe(true));
  });

  it("changes a detail under More, and the preview follows", async () => {
    const { client } = await shown();
    fireEvent.click(editor().getByRole("button", { name: "More" }));

    fireEvent.click(within(editor().getByRole("group", { name: "Corners" })).getByRole("radio", { name: "Rounded" }));

    await waitFor(() => expect(client.lastUpdate("t1")?.tokens.corners).toBe("rounded"));
    expect(screen.getByRole("region", { name: "Preview" }).querySelector(".look-preview")?.getAttribute("data-corners")).toBe("rounded");
  });

  it("makes another theme the default, and deletes one after asking", async () => {
    const { client } = await shown();

    fireEvent.click(list().getByRole("button", { name: "Acme Event" }));
    fireEvent.click(editor().getByRole("button", { name: "Make default" }));
    await waitFor(() => expect(client.calls).toContainEqual(["makeDefault", "t2"]));
    expect(await list().findByRole("button", { name: "Acme Event Default" })).toBeTruthy();
    fireEvent.click(list().getByRole("button", { name: "Acme Standard" }));
    fireEvent.click(editor().getByRole("button", { name: "Delete" }));
    expect(editor().getByText("Delete Acme Standard? Surveys that use it get the default look.")).toBeTruthy();
    fireEvent.click(editor().getByRole("button", { name: "Delete theme" }));

    await waitFor(() => expect(client.calls).toContainEqual(["remove", "t1"]));
    expect(list().queryByRole("button", { name: "Acme Standard" })).toBeNull();
  });

  it("does not offer Delete for the default theme", async () => {
    await shown();

    expect(editor().queryByRole("button", { name: "Delete" })).toBeNull();
    expect(editor().getByText("Surveys without their own theme use this one.")).toBeTruthy();
  });

  it("adds a new theme from a preset", async () => {
    const { client } = await shown();

    fireEvent.click(list().getByRole("button", { name: "New theme" }));
    fireEvent.click(screen.getByRole("button", { name: "Start from Warm" }));

    await waitFor(() => expect(client.calls).toContainEqual(["create", "Warm", themePresets[2]!.tokens]));
    expect(await list().findByRole("button", { name: "Warm" })).toBeTruthy();
  });

  it("numbers a second theme from the same preset", async () => {
    const { client } = await shown(new FakeThemesClient([{ id: "t1", name: "Warm", isDefault: true, tokens: themePresets[2]!.tokens }]));

    fireEvent.click(list().getByRole("button", { name: "New theme" }));
    fireEvent.click(screen.getByRole("button", { name: "Start from Warm" }));

    await waitFor(() => expect(client.calls).toContainEqual(["create", "Warm 2", themePresets[2]!.tokens]));
  });

  it("shows the preview on a stage like a survey's Look: Phone, Tablet, Desktop, zoom and Ctrl + scroll (W32)", async () => {
    await shown();
    const preview = within(screen.getByRole("region", { name: "Preview" }));
    const page = () => screen.getByRole("region", { name: "Preview" }).querySelector(".look-preview")!.parentElement!;

    expect(preview.getByRole("button", { name: "Desktop" }).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(preview.getByRole("button", { name: "Phone" }));
    expect(page().style.width).toBe("390px");
    fireEvent.click(preview.getByRole("button", { name: "Zoom in" }));
    expect(preview.getByText("125 %")).toBeTruthy();
    const wheel = new WheelEvent("wheel", { deltaY: 100, ctrlKey: true, cancelable: true, bubbles: true });
    preview.getByRole("region", { name: "Zoom" }).dispatchEvent(wheel);
    expect(wheel.defaultPrevented).toBe(true);
    expect(await preview.findByText("114 %")).toBeTruthy();
    fireEvent.click(preview.getByRole("button", { name: "Fit" }));
    expect(preview.getByText("100 %")).toBeTruthy();
  });

  it("speaks German", async () => {
    await shown(new FakeThemesClient(), "de");

    expect(screen.getByRole("heading", { level: 1, name: "Looks" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Mehr" })).toBeTruthy();
  });

  it("has no accessibility violations, also with More open", async () => {
    const { container } = await shown();
    fireEvent.click(editor().getByRole("button", { name: "More" }));

    await expectNoAxeViolations(container);
  });
});
