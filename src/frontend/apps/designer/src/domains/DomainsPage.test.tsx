// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DomainsPage } from "./DomainsPage";
import { FakeDomainsClient } from "./testing";

afterEach(cleanup);

async function shown(client = new FakeDomainsClient(), locale: "de" | "en" = "en") {
  const writeClipboard = vi.fn(() => Promise.resolve());
  const view = render(<DomainsPage client={client} locale={locale} writeClipboard={writeClipboard} timeZone="UTC" now={() => new Date("2026-10-14T10:00:00Z")} />);
  await screen.findByRole("heading", { level: 1 });
  await act(async () => {});

  return { client, writeClipboard, ...view };
}

const row = (hostname: string) => within(screen.getByRole("region", { name: hostname }));

describe("Domains (AWK-7, W12 A): one screen", () => {
  it("lists the system address and the organisation's domains with their state", async () => {
    const { container } = await shown();

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Domains");
    expect(screen.getAllByRole("region").map((region) => region.getAttribute("aria-label"))).toEqual(["surveys.askwerk.app", "surveys.acme.at", "umfragen.acme.at", "survey.acme.com"]);
    expect(row("surveys.askwerk.app").getByText("System")).toBeTruthy();
    expect(row("surveys.acme.at").getByText("Primary")).toBeTruthy();
    expect(row("surveys.acme.at").getByText("Verified")).toBeTruthy();
    expect(row("umfragen.acme.at").getByText("Waiting for DNS")).toBeTruthy();
    expect(row("survey.acme.com").getByText("Not found after 7 days")).toBeTruthy();
    await expectNoAxeViolations(container);
  });

  it("shows a waiting domain's two records to copy, and what the last check found", async () => {
    const { writeClipboard } = await shown();

    const records = within(row("umfragen.acme.at").getByRole("table", { name: "DNS records" }));
    expect(records.getAllByRole("row").slice(1).map((each) => within(each).getAllByRole("cell").slice(0, 3).map((cell) => cell.textContent))).toEqual([
      ["CNAME", "umfragen.acme.at", "surveys.askwerk.app"],
      ["TXT", "_askwerk.umfragen.acme.at", "askwerk-verify=9f3c00000000000000000000000000a21e"],
    ]);
    expect(row("umfragen.acme.at").getByText("Last check 2 min ago: CNAME found ✓ · TXT not found yet")).toBeTruthy();
    fireEvent.click(records.getByRole("button", { name: "Copy TXT value" }));

    expect(writeClipboard).toHaveBeenCalledWith("askwerk-verify=9f3c00000000000000000000000000a21e");
  });

  it("verifies a waiting domain with one click", async () => {
    const { client } = await shown();

    fireEvent.click(row("umfragen.acme.at").getByRole("button", { name: "Verify" }));

    await waitFor(() => expect(row("umfragen.acme.at").getByText("Verified")).toBeTruthy());
    expect(client.calls).toContainEqual(["verify", "d2"]);
  });

  it("adds a domain, and says why one cannot be added", async () => {
    const { client } = await shown();
    const field = screen.getByRole("textbox", { name: "Add a domain" });

    fireEvent.change(field, { target: { value: "https://x/y" } });
    fireEvent.click(screen.getByRole("button", { name: "Add domain" }));
    expect((await screen.findByRole("alert")).textContent).toBe("Enter a hostname such as surveys.your-company.com, without https:// or a path.");
    fireEvent.change(field, { target: { value: "surveys.acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Add domain" }));
    expect((await screen.findByRole("alert")).textContent).toBe("This domain is already in use.");
    fireEvent.change(field, { target: { value: "neu.acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Add domain" }));

    await screen.findByRole("region", { name: "neu.acme.at" });
    expect(client.calls).toContainEqual(["add", "neu.acme.at"]);
    expect((field as HTMLInputElement).value).toBe("");
  });

  it("makes a verified domain or the system address primary", async () => {
    const { client } = await shown();

    fireEvent.click(row("surveys.askwerk.app").getByRole("button", { name: "Make primary" }));

    await waitFor(() => expect(row("surveys.askwerk.app").getByText("Primary")).toBeTruthy());
    expect(client.calls).toContainEqual(["setPrimary", null]);
    expect(row("surveys.acme.at").getByRole("button", { name: "Make primary" })).toBeTruthy();
    expect(row("umfragen.acme.at").queryByRole("button", { name: "Make primary" })).toBeNull();
  });

  it("removes a domain after asking", async () => {
    const { client } = await shown();

    fireEvent.click(row("survey.acme.com").getByRole("button", { name: "Remove" }));
    expect(row("survey.acme.com").getByText("Remove survey.acme.com? Links on it stop working.")).toBeTruthy();
    fireEvent.click(row("survey.acme.com").getByRole("button", { name: "Remove domain" }));

    await waitFor(() => expect(screen.queryByRole("region", { name: "survey.acme.com" })).toBeNull());
    expect(client.calls).toContainEqual(["remove", "d3"]);
  });

  it("lists the websites that may show the surveys, one per line, and saves them", async () => {
    const { client } = await shown();
    const field = await screen.findByRole("textbox", { name: "Websites that may show your surveys" });

    expect((field as HTMLTextAreaElement).value).toBe("https://www.acme.at");
    fireEvent.change(field, { target: { value: "https://www.acme.at\n\nhttps://intranet.acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Save websites" }));

    expect(await screen.findByText("Saved")).toBeTruthy();
    expect(client.calls).toContainEqual(["setEmbedding", ["https://www.acme.at", "https://intranet.acme.at"]]);
  });

  it("lets the designer be embedded only on the websites listed, off by default (AWK-13)", async () => {
    const { client, container } = await shown();

    expect(screen.getByRole("radio", { name: "Off" })).toHaveProperty("checked", true);
    fireEvent.click(screen.getByRole("radio", { name: "Allow on these websites" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Allow on these websites" }), { target: { value: "https://hr.acme.at" } });
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "Save designer websites" }));

    await waitFor(() => expect(client.calls).toContainEqual(["setDesignerEmbedding", ["https://hr.acme.at"]]));
  });

  it("says how to write a website that cannot be saved", async () => {
    await shown();

    fireEvent.change(await screen.findByRole("textbox", { name: "Websites that may show your surveys" }), { target: { value: "www.acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Save websites" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Write each website as https://www.example.com, one per line.");
  });

  it("speaks German", async () => {
    await shown(new FakeDomainsClient(), "de");

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Domains");
    expect(row("umfragen.acme.at").getByText("Wartet auf DNS")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Domain hinzufügen" })).toBeTruthy();
  });
});
