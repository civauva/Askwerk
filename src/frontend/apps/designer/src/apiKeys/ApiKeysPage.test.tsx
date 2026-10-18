// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiKeysPage } from "./ApiKeysPage";
import { FakeApiKeysClient } from "./testing";

afterEach(cleanup);

async function shown(client = new FakeApiKeysClient(), locale: "de" | "en" = "en") {
  const writeClipboard = vi.fn(() => Promise.resolve());
  const view = render(<ApiKeysPage client={client} locale={locale} writeClipboard={writeClipboard} timeZone="UTC" now={() => new Date("2026-10-15T10:00:00Z")} />);
  await screen.findByRole("heading", { level: 1 });
  await act(async () => {});

  return { client, writeClipboard, ...view };
}

const dialog = (name: string) => within(screen.getByRole("dialog", { name }));

describe("Settings → API (AWK-25, W14 C and D)", () => {
  it("lists the organisation's keys with their access, use and expiry", async () => {
    const { container } = await shown();

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("API");
    expect(screen.getByText("Keys let other systems read this organisation's surveys and results through the Public API. A key is shown only once.")).toBeTruthy();
    expect(screen.getByRole("link", { name: "API documentation (OpenAPI)" }).getAttribute("href")).toBe("/api/v1/openapi.json");
    const grid = within(screen.getByRole("grid", { name: "API keys" }));
    expect(grid.getAllByRole("row").slice(1).map((row) => within(row).getAllByRole("gridcell").slice(0, 6).map((cell) => cell.textContent))).toEqual([
      ["Reporting server", "Read results", "awk_7Hq2…", "Oct 1, 2026", "Oct 14, 2026", "Dec 30, 2026"],
      ["HR system sync", "Full access", "awk_Lm9x…", "Sep 14, 2026", "never", "never"],
      ["Old intranet", "Custom", "awk_c41T…", "Mar 2, 2026", "Apr 3, 2026", "expired"],
    ]);
    await expectNoAxeViolations(container);
  });

  it("invites to create the first key", async () => {
    const client = new FakeApiKeysClient();
    client.keys = [];
    await shown(client);

    expect(screen.getByText("No keys yet. Create one to connect another system.")).toBeTruthy();
    expect(screen.queryByRole("grid")).toBeNull();
  });

  it("creates a key to read results in 90 days by default, then shows it once to copy", async () => {
    const { client, writeClipboard, container } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Create key" }));
    const create = dialog("Create key");
    expect((create.getByRole("radio", { name: /^Read results/ }) as HTMLInputElement).checked).toBe(true);
    fireEvent.change(create.getByRole("textbox", { name: "Name" }), { target: { value: "Reporting server 2" } });
    fireEvent.click(create.getByRole("button", { name: "Create" }));

    const created = within(await screen.findByRole("dialog", { name: "Key created" }));
    expect(created.getByText("Copy the key now. It is not shown again.")).toBeTruthy();
    expect(created.getByText("awk_Zz00secretsecretsecretsecretsecretsecret0000")).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(created.getByRole("button", { name: "Copy" }));
    expect(writeClipboard).toHaveBeenCalledWith("awk_Zz00secretsecretsecretsecretsecretsecret0000");
    fireEvent.click(created.getByRole("button", { name: "Done" }));

    expect(client.calls.find(([name]) => name === "create")).toEqual(["create", { name: "Reporting server 2", scopes: ["surveys:read", "responses:read"], expiresInDays: 90 }]);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByText("awk_Zz00secretsecretsecretsecretsecretsecret0000")).toBeNull();
    expect(await screen.findByText("awk_Zz00…")).toBeTruthy();
  });

  it("creates a key with full access that never expires, with the details under More", async () => {
    const { client } = await shown();
    fireEvent.click(screen.getByRole("button", { name: "Create key" }));
    const create = dialog("Create key");

    fireEvent.change(create.getByRole("textbox", { name: "Name" }), { target: { value: "HR sync 2" } });
    fireEvent.click(create.getByRole("radio", { name: /^Full access/ }));
    fireEvent.click(create.getByRole("button", { name: "More" }));
    expect((create.getByRole("checkbox", { name: "webhooks:manage" }) as HTMLInputElement).checked).toBe(true);
    fireEvent.click(create.getByRole("button", { name: /^Expires/ }));
    fireEvent.click(screen.getByRole("option", { name: "never" }));
    fireEvent.click(create.getByRole("button", { name: "Create" }));
    await screen.findByRole("dialog", { name: "Key created" });

    expect(client.calls.find(([name]) => name === "create")?.[1]).toEqual({
      name: "HR sync 2",
      scopes: ["surveys:read", "surveys:write", "responses:read", "responses:write", "participants:read", "participants:write", "contacts:read", "contacts:write", "webhooks:manage"],
      expiresInDays: null,
    });
  });

  it("asks for a name", async () => {
    await shown();
    fireEvent.click(screen.getByRole("button", { name: "Create key" }));

    fireEvent.click(dialog("Create key").getByRole("button", { name: "Create" }));

    expect(await dialog("Create key").findByText("Enter a name.")).toBeTruthy();
  });

  it("revokes a key after asking", async () => {
    const { client } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Revoke Reporting server" }));
    const ask = dialog("Revoke key");
    expect(ask.getByText("Revoke “Reporting server”? Systems using it stop working at once.")).toBeTruthy();
    fireEvent.click(ask.getByRole("button", { name: "Revoke" }));

    await waitFor(() => expect(screen.queryByText("Reporting server")).toBeNull());
    expect(client.calls).toContainEqual(["revoke", "k1"]);
  });

  it("speaks German", async () => {
    await shown(new FakeApiKeysClient(), "de");

    expect(screen.getByRole("button", { name: "Schlüssel erstellen" })).toBeTruthy();
    expect(within(screen.getByRole("grid", { name: "API-Schlüssel" })).getByText("Ergebnisse lesen")).toBeTruthy();
  });
});
