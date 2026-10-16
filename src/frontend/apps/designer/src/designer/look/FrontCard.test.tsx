// SPDX-License-Identifier: AGPL-3.0-only
import { themePresets, type ThemeTokens } from "@askwerk/schema";
import { cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { FrontCard } from "./FrontCard";

afterEach(cleanup);

const base = themePresets[0]!.tokens as ThemeTokens;
const assetUrl = (assetId: string, width: number) => `/assets/${assetId}?w=${width}`;

describe("A survey's front page in miniature (W32)", () => {
  it("draws the organisation bar, the welcome card and Start, as the runner does", () => {
    const { container } = render(<FrontCard tokens={base} title="Customer satisfaction" text="Three minutes." organisation="Acme GmbH" start="Start" />);

    expect(container.querySelector(".fc-head")?.textContent).toBe("AAcme GmbH");
    expect(container.querySelector(".fc-card .fc-title")?.textContent).toBe("Customer satisfaction");
    expect(container.querySelector(".fc-card .fc-go")?.textContent).toBe("Start");
  });

  it("follows the look: welcome on a band, outline buttons, logo, and the survey's own header picture first", () => {
    const tokens = { ...base, layout: "header-image", buttons: "outline", logo: { asset: "logo1" }, headerImage: { asset: "look-picture" }, pages: { welcome: { layout: "band" } } } as ThemeTokens;
    const { container } = render(<FrontCard tokens={tokens} title="Event" text="" organisation="Acme" start="Start" headerAsset="own-picture" assetUrl={assetUrl} />);
    const card = container.querySelector(".fc")!;

    expect(card.getAttribute("data-welcome")).toBe("band");
    expect(card.getAttribute("data-buttons")).toBe("outline");
    expect(container.querySelector(".fc-head img")?.getAttribute("src")).toBe("/assets/logo1?w=160");
    expect((container.querySelector(".fc-picture") as HTMLElement).style.backgroundImage).toContain("/assets/own-picture?w=480");
  });

  it("hides the organisation bar when the look does", () => {
    const { container } = render(<FrontCard tokens={{ ...base, pages: { header: { show: false } } }} title="T" text="" organisation="Acme" start="Start" />);

    expect(container.querySelector(".fc-head")).toBeNull();
  });
});
