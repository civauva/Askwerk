// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyApprovals } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApprovalPill, ApprovalSlot, approvalLine, workspaceText } from "./index";

afterEach(cleanup);

const now = () => new Date("2026-10-15T12:00:00Z");
const none: SurveyApprovals = { fingerprint: "7f3a91c2", state: "none", items: [] };
const ben = { id: "a1", by: "Ben Huber", at: "2026-10-15T10:50:00Z", note: "checked with the works council", fingerprint: "7f3a91c2", version: 4, current: true, mine: false };
const clara = { id: "a2", by: "Clara Wolf", at: "2026-10-14T16:20:00Z", note: null, fingerprint: "1b0e44d7", version: 4, current: false, mine: false };
const approved: SurveyApprovals = { fingerprint: "7f3a91c2", state: "approved", items: [ben, clara] };
const earlier: SurveyApprovals = { fingerprint: "7f3a91c2", state: "earlier", items: [clara] };

describe("Approve in Preview (AWK-28, W16 A, B)", () => {
  it("approves the draft as Preview shows it, with an optional note", async () => {
    const onApprove = vi.fn(() => Promise.resolve());
    const { container } = render(
      <ApprovalSlot t={workspaceText("de")} approvals={none} title="Kundenzufriedenheit 2026" languages={["de", "en"]} timeZone="UTC" now={now} onApprove={onApprove} onWithdraw={() => Promise.resolve()} />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Freigeben…" }));
    const dialog = within(screen.getByRole("dialog", { name: "Diesen Stand freigeben" }));
    expect(dialog.getByText("Kundenzufriedenheit 2026")).toBeTruthy();
    expect(dialog.getByText("Ändert ihn danach jemand, gilt die Freigabe nicht für die Änderung.")).toBeTruthy();
    fireEvent.change(dialog.getByRole("textbox", { name: "Notiz (optional)" }), { target: { value: "geprüft mit dem Betriebsrat" } });
    fireEvent.click(dialog.getByRole("button", { name: "Freigeben" }));

    await waitFor(() => expect(onApprove).toHaveBeenCalledWith("geprüft mit dem Betriebsrat"));
    expect(screen.queryByRole("dialog")).toBeNull();
    await expectNoAxeViolations(container);
  });

  it("says the draft is approved by you, and withdraws it", async () => {
    const onWithdraw = vi.fn(() => Promise.resolve());
    render(
      <ApprovalSlot
        t={workspaceText("en")}
        approvals={{ ...approved, items: [{ ...ben, mine: true }] }}
        title="Customer satisfaction 2026"
        languages={["de", "en"]}
        timeZone="UTC"
        now={now}
        onApprove={() => Promise.resolve()}
        onWithdraw={onWithdraw}
      />,
    );

    expect(screen.getByText("✓ Approved by you · today 10:50 · this draft")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Approve…" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Withdraw" }));

    await waitFor(() => expect(onWithdraw).toHaveBeenCalledWith("a1"));
  });

  it("still lets you approve when someone else did", () => {
    render(<ApprovalSlot t={workspaceText("en")} approvals={approved} title="Customer satisfaction 2026" languages={["en"]} timeZone="UTC" now={now} onApprove={() => Promise.resolve()} onWithdraw={() => Promise.resolve()} />);

    expect(screen.getByText("✓ Approved by Ben Huber · today 10:50 · this draft")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Approve…" })).toBeTruthy();
  });
});

describe("The approval next to Publish (AWK-28, W16 C)", () => {
  it("names the latest approval of this draft and lists every approval with its note", async () => {
    const { container } = render(<ApprovalPill t={workspaceText("en")} approvals={approved} timeZone="UTC" now={now} />);

    fireEvent.click(screen.getByRole("button", { name: "✓ Approved · Ben Huber, today 10:50" }));
    const list = within(screen.getByRole("dialog", { name: "Approvals" }));
    expect(list.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "Ben Huber today 10:50 · this draft“checked with the works council”",
      "Clara Wolf yesterday 16:20 · earlier draft",
    ]);
    await expectNoAxeViolations(container);
  });

  it("says when only an earlier draft is approved, or none", () => {
    render(<ApprovalPill t={workspaceText("en")} approvals={earlier} timeZone="UTC" now={now} />);
    expect(screen.getByRole("button", { name: "! Approved an earlier draft" })).toBeTruthy();
    cleanup();

    render(<ApprovalPill t={workspaceText("de")} approvals={none} timeZone="UTC" now={now} />);
    expect(screen.getByRole("button", { name: "Nicht freigegeben" })).toBeTruthy();
  });
});

describe("The Publish dialog's line (AWK-28, W16 D)", () => {
  it("says what there is, and never asks for anything", () => {
    const t = workspaceText("en");

    expect([approvalLine(t, approved), approvalLine(t, earlier), approvalLine(t, none)]).toEqual([
      "✓ Approved by Ben Huber for this draft.",
      "An earlier draft was approved; this one is not.",
      "Not approved.",
    ]);
  });
});
