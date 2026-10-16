// SPDX-License-Identifier: AGPL-3.0-only
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { CodeInput } from "./index";
import { expectNoAxeViolations } from "./testing";

afterEach(cleanup);

function Code({ invalid = false }: { invalid?: boolean }) {
  const [value, setValue] = useState("");

  return (
    <>
      <span id="code-label">Code from your app</span>
      <CodeInput labelledBy="code-label" value={value} onChange={setValue} invalid={invalid} digitLabel={(n) => `Digit ${n} of 6`} />
      <output data-testid="value">{value}</output>
    </>
  );
}

const box = (n: number) => screen.getByRole("textbox", { name: `Digit ${n} of 6` }) as HTMLInputElement;

describe("CodeInput (AWK-16, M1)", () => {
  it("shows six boxes in one group named by the label", () => {
    render(<Code />);

    const group = screen.getByRole("group", { name: "Code from your app" });
    expect(within(group).getAllByRole("textbox")).toHaveLength(6);
    expect(box(1).getAttribute("autocomplete")).toBe("one-time-code");
    expect(box(1).getAttribute("inputmode")).toBe("numeric");
  });

  it("moves to the next box with each digit, and ignores anything else", () => {
    render(<Code />);

    box(1).focus();
    fireEvent.change(box(1), { target: { value: "9" } });
    fireEvent.change(box(2), { target: { value: "a" } });
    fireEvent.change(box(2), { target: { value: "1" } });

    expect(screen.getByTestId("value").textContent).toBe("91");
    expect(document.activeElement).toBe(box(3));
  });

  it("fills every box from a pasted or autofilled code", () => {
    render(<Code />);

    fireEvent.change(box(1), { target: { value: "482 913" } });

    expect(screen.getByTestId("value").textContent).toBe("482913");
    expect([1, 2, 3, 4, 5, 6].map((n) => box(n).value).join("")).toBe("482913");
    expect(document.activeElement).toBe(box(6));
  });

  it("goes back with Backspace from an empty box, and with the arrows", () => {
    render(<Code />);
    fireEvent.change(box(1), { target: { value: "48" } });

    fireEvent.keyDown(box(3), { key: "Backspace" });
    expect(screen.getByTestId("value").textContent).toBe("4");
    expect(document.activeElement).toBe(box(2));
    fireEvent.keyDown(box(2), { key: "ArrowLeft" });
    expect(document.activeElement).toBe(box(1));
    fireEvent.keyDown(box(1), { key: "ArrowRight" });
    expect(document.activeElement).toBe(box(2));
  });

  it("marks every box when the code is wrong, and has no axe violations", async () => {
    const { container } = render(<Code invalid />);

    expect([1, 2, 3, 4, 5, 6].every((n) => box(n).getAttribute("aria-invalid") === "true")).toBe(true);
    await expectNoAxeViolations(container);
  });
});
