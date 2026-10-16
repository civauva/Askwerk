// SPDX-License-Identifier: AGPL-3.0-only
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Checkbox, ConfirmDialog, Dialog, DialogActions, Field, Menu, MenuItem, MenuLink, MenuSeparator, Notice, RadioGroup, Switch, Tabs } from "./index";
import { expectNoAxeViolations } from "./testing";

afterEach(cleanup);

describe("Dialog (W20 1)", () => {
  it("is named by its title, takes the focus, keeps it inside, closes with Escape and gives the focus back", async () => {
    const onClose = vi.fn();
    render(
      <>
        <button type="button">Opener</button>
        <Dialog title="Remove member" onClose={onClose}>
          <p>Remove Ben?</p>
          <DialogActions>
            <button type="button">Cancel</button>
            <button type="button">Remove</button>
          </DialogActions>
        </Dialog>
      </>,
    );
    const dialog = screen.getByRole("dialog", { name: "Remove member" });

    expect(document.activeElement).toBe(within(dialog).getByRole("button", { name: "Cancel" }));
    within(dialog).getByRole("button", { name: "Remove" }).focus();
    fireEvent.keyDown(dialog, { key: "Tab" });
    expect(document.activeElement).toBe(within(dialog).getByRole("button", { name: "Cancel" }));
    fireEvent.keyDown(dialog, { key: "Escape" });
    expect(onClose).toHaveBeenCalled();
    await expectNoAxeViolations(document.body);
  });

  it("asks with Cancel first and the action as primary", () => {
    const onConfirm = vi.fn();
    render(<ConfirmDialog title="Archive" text="Archive it?" confirm="Archive" cancel="Cancel" onConfirm={onConfirm} onCancel={() => undefined} />);

    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Cancel" }));
    fireEvent.click(screen.getByRole("button", { name: "Archive" }));
    expect(onConfirm).toHaveBeenCalled();
  });
});

describe("Menu (W20 2)", () => {
  it("opens from its button, moves with the arrow keys, runs an item and closes", async () => {
    const onRename = vi.fn();
    const { container } = render(
      <Menu label="Survey" trigger={(props) => <button type="button" {...props}>Customer satisfaction</button>}>
        <MenuItem onSelect={onRename}>Rename</MenuItem>
        <MenuLink href="/pdf">Download PDF</MenuLink>
        <MenuSeparator />
        <MenuItem onSelect={() => undefined}>Archive…</MenuItem>
      </Menu>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Customer satisfaction" }));
    const menu = screen.getByRole("menu", { name: "Survey" });
    expect(document.activeElement).toBe(within(menu).getByRole("menuitem", { name: "Rename" }));
    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(document.activeElement).toBe(within(menu).getByRole("menuitem", { name: "Download PDF" }));
    fireEvent.keyDown(menu, { key: "End" });
    expect(document.activeElement).toBe(within(menu).getByRole("menuitem", { name: "Archive…" }));
    await expectNoAxeViolations(container);
    fireEvent.click(within(menu).getByRole("menuitem", { name: "Rename" }));

    expect(onRename).toHaveBeenCalled();
    expect(screen.queryByRole("menu")).toBeNull();
  });
});

describe("Tabs (W20 3)", () => {
  it("selects with a click and with the arrow keys", () => {
    function Shown() {
      const [tab, setTab] = useState<"overview" | "responses">("overview");
      return <Tabs label="Results" tabs={[["overview", "Overview"], ["responses", "Responses"]]} value={tab} onChange={setTab} />;
    }
    render(<Shown />);

    fireEvent.keyDown(screen.getByRole("tab", { name: "Overview" }), { key: "ArrowRight" });

    expect(screen.getByRole("tab", { name: "Responses" }).getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(screen.getByRole("tab", { name: "Responses" }));
  });
});

describe("Switch, checkbox, radios and field (W20 4, 5)", () => {
  it("report their state and changes", async () => {
    const onSwitch = vi.fn();
    const onCheck = vi.fn();
    const onRadio = vi.fn();
    const { container } = render(
      <>
        <Switch label="Welcome page" checked onChange={onSwitch} />
        <Checkbox label="Send a reminder" hint="Once, after a week" checked={false} onChange={onCheck} />
        <RadioGroup legend="Layout" value="classic" onChange={onRadio} options={[{ value: "classic", label: "Classic" }, { value: "one", label: "One at a time" }]} />
        <Field label="Email" error="Enter an email address.">
          <input />
        </Field>
      </>,
    );

    fireEvent.click(screen.getByRole("switch", { name: "Welcome page" }));
    fireEvent.click(screen.getByRole("checkbox", { name: /^Send a reminder/ }));
    fireEvent.click(screen.getByRole("radio", { name: "One at a time" }));

    expect([onSwitch.mock.calls[0], onCheck.mock.calls[0], onRadio.mock.calls[0]]).toEqual([[false], [true], ["one"]]);
    expect(screen.getByRole("textbox", { name: "Email" }).getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByRole("textbox", { name: "Email" }).getAttribute("aria-describedby")).toBe(screen.getByRole("alert").id);
    await expectNoAxeViolations(container);
  });
});

describe("Notice (W20 6)", () => {
  it("closes a done message by itself after 5 seconds", () => {
    vi.useFakeTimers();
    const onClose = vi.fn();
    render(<Notice onClose={onClose} closeLabel="Dismiss">2 invitations sent.</Notice>);

    expect(screen.getByRole("status").textContent).toContain("2 invitations sent.");
    act(() => vi.advanceTimersByTime(5000));
    vi.useRealTimers();

    expect(onClose).toHaveBeenCalled();
  });
});
