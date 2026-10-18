// SPDX-License-Identifier: AGPL-3.0-only
import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { RowTools } from "./RowTools";

describe("RowTools (W42b B): a row's actions as one small toolbar", () => {
  it("names each icon for screen readers, shows its tooltip when pointed at or focused, and runs it", () => {
    const suspend = vi.fn();
    const remove = vi.fn();
    render(
      <RowTools
        label="Acme GmbH"
        tools={[
          { icon: "circle-pause", label: "Suspend", name: "Suspend: Acme GmbH", onClick: suspend },
          { icon: "trash-2", label: "Delete", name: "Delete: Acme GmbH", danger: true, onClick: remove },
        ]}
      />,
    );
    const group = within(screen.getByRole("group", { name: "Acme GmbH" }));

    fireEvent.pointerEnter(group.getByRole("button", { name: "Suspend: Acme GmbH" }));
    expect(screen.getByRole("tooltip").textContent).toBe("Suspend");
    fireEvent.click(group.getByRole("button", { name: "Suspend: Acme GmbH" }));
    expect(screen.queryByRole("tooltip")).toBeNull();
    fireEvent.focus(group.getByRole("button", { name: "Delete: Acme GmbH" }));
    expect(screen.getByRole("tooltip").textContent).toBe("Delete");
    fireEvent.click(group.getByRole("button", { name: "Delete: Acme GmbH" }));

    expect([suspend.mock.calls.length, remove.mock.calls.length]).toEqual([1, 1]);
    expect(group.getByRole("button", { name: "Delete: Acme GmbH" }).className).toContain("aw-row-tools__tool--danger");
  });

  it("draws nothing without actions", () => {
    const { container } = render(<RowTools label="Row" tools={[]} />);

    expect(container.innerHTML).toBe("");
  });
});
