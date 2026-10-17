// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { FakeDomainsClient } from "../domains/testing";
import { DropInPage, dropInCode } from "./DropInPage";

afterEach(cleanup);

describe("Drop-in components (W37)", () => {
  it("shows the allowed sites, saves the host look and gives the code for the chosen components", async () => {
    const domains = new FakeDomainsClient();
    domains.origins = ["https://portal.acme.at"];
    domains.designerOrigins = ["https://intranet.acme.at"];
    const writeClipboard = vi.fn(() => Promise.resolve());
    const { container } = render(<DropInPage domains={domains} locale="en" baseUrl="https://surveys.acme.at" onNavigate={() => undefined} writeClipboard={writeClipboard} />);

    expect(await screen.findByText(/portal\.acme\.at · intranet\.acme\.at/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /<askwerk-designer>/ }));
    fireEvent.change(screen.getByRole("textbox", { name: /Font/ }), { target: { value: "Segoe UI, sans-serif" } });
    fireEvent.change(screen.getByRole("spinbutton", { name: /Corners/ }), { target: { value: "4" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByText(/Saved: the components follow this look/);

    const code = screen.getByText(/components\.js/).textContent ?? "";
    expect(domains.calls).toContainEqual(["setHostLook", { accent: null, background: null, font: "Segoe UI, sans-serif", radius: 4 }]);
    expect(code).toContain('<script src="https://surveys.acme.at/s/components.js" type="module"></script>');
    expect(code).toContain("--askwerk-radius: 4px;");
    expect(code).not.toContain("<askwerk-designer");
    expect(code).toContain("<askwerk-surveys");
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));
    await waitFor(() => expect(writeClipboard).toHaveBeenCalledWith(code));
    await expectNoAxeViolations(container);
  });

  it("writes look=\"askwerk\" when the components keep Askwerk's look", () => {
    expect(dropInCode("https://s.example.org", ["survey"], "askwerk", { accent: null, background: null, font: null, radius: null })).toContain('<askwerk-survey survey="CODE" identity="TOKEN_FROM_YOUR_SERVER" look="askwerk"></askwerk-survey>');
  });
});
