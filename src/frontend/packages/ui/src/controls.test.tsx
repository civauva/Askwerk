// SPDX-License-Identifier: AGPL-3.0-only
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { formatDate, parseDate, parseTime } from "./dates";
import { ComboBox, DatePicker, TimeField, type ComboBoxTexts, type DatePickerTexts } from "./index";
import { expectNoAxeViolations } from "./testing";

afterEach(cleanup);

const comboTexts: ComboBoxTexts = {
  choose: "Choose…",
  search: "Search",
  results: (shown, total) => `${shown} of ${total}`,
  noResults: "Nothing matches.",
};

const branches = ["Wien Mitte", "Wien Favoriten", "Wiener Neustadt", "Graz", "Linz", "Salzburg", "Innsbruck", "Klagenfurt", "Bregenz", "Eisenstadt", "St. Pölten", "Villach"].map(
  (label, index) => ({ value: `B${index + 1}`, label }),
);

function Combo({ options = branches, initial = null as string | null }) {
  const [value, setValue] = useState<string | null>(initial);

  return (
    <>
      <span id="branch-label">Branch</span>
      <ComboBox labelledBy="branch-label" options={options} value={value} onChange={setValue} texts={comboTexts} />
      <output data-testid="value">{value ?? "none"}</output>
    </>
  );
}

const trigger = () => screen.getByRole("button", { name: /Branch/ });

describe("ComboBox", () => {
  it("shows the choice, or the prompt when there is none", () => {
    render(<Combo initial="B4" />);

    expect(trigger().textContent).toContain("Graz");
    expect(trigger().getAttribute("aria-haspopup")).toBe("listbox");
    expect(trigger().getAttribute("aria-expanded")).toBe("false");
  });

  it("filters while typing, ignoring case and accents, and picks with Enter", () => {
    render(<Combo />);

    fireEvent.click(trigger());
    const search = screen.getByRole("combobox", { name: "Search" });
    fireEvent.change(search, { target: { value: "WIE" } });

    expect(within(screen.getByRole("listbox")).getAllByRole("option").map((option) => option.textContent)).toEqual(["Wien Mitte", "Wien Favoriten", "Wiener Neustadt"]);
    expect(screen.getByText("3 of 12")).toBeTruthy();
    fireEvent.keyDown(search, { key: "ArrowDown" });
    fireEvent.keyDown(search, { key: "Enter" });

    expect(screen.getByTestId("value").textContent).toBe("B2");
    expect(screen.queryByRole("listbox")).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it("matches without accents", () => {
    render(<Combo />);

    fireEvent.click(trigger());
    fireEvent.change(screen.getByRole("combobox", { name: "Search" }), { target: { value: "polten" } });

    expect(within(screen.getByRole("listbox")).getAllByRole("option").map((option) => option.textContent)).toEqual(["St. Pölten"]);
  });

  it("says so when nothing matches", () => {
    render(<Combo />);

    fireEvent.click(trigger());
    fireEvent.change(screen.getByRole("combobox", { name: "Search" }), { target: { value: "Berlin" } });

    expect(screen.getByText("Nothing matches.")).toBeTruthy();
  });

  it("has no search for short lists; arrows and Enter work on the list itself", () => {
    render(<Combo options={branches.slice(0, 3)} />);

    fireEvent.keyDown(trigger(), { key: "ArrowDown" });
    const list = screen.getByRole("listbox");

    expect(screen.queryByRole("combobox")).toBeNull();
    expect(document.activeElement).toBe(list);
    fireEvent.keyDown(list, { key: "ArrowDown" });
    fireEvent.keyDown(list, { key: "Enter" });
    expect(screen.getByTestId("value").textContent).toBe("B2");
  });

  it("marks the chosen option and points the active one out to screen readers", () => {
    render(<Combo initial="B3" />);

    fireEvent.click(trigger());
    const chosen = screen.getByRole("option", { name: "Wiener Neustadt" });

    expect(chosen.getAttribute("aria-selected")).toBe("true");
    expect(screen.getByRole("combobox", { name: "Search" }).getAttribute("aria-activedescendant")).toBe(chosen.id);
  });

  it("closes with Escape without changing the choice", () => {
    render(<Combo initial="B4" />);

    fireEvent.click(trigger());
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Escape" });

    expect(screen.queryByRole("listbox")).toBeNull();
    expect(screen.getByTestId("value").textContent).toBe("B4");
  });

  it("closes on a click outside, without changing the choice", () => {
    render(<Combo initial="B4" />);

    fireEvent.click(trigger());
    fireEvent.mouseDown(document.body);

    expect(screen.queryByRole("listbox")).toBeNull();
    expect(screen.getByTestId("value").textContent).toBe("B4");
  });

  it("closes on Escape wherever the focus is, and gives the focus back to the field", () => {
    render(<Combo />);

    fireEvent.click(trigger());
    trigger().focus();
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });

    expect(screen.queryByRole("listbox")).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it("closes when the focus moves on to something else", () => {
    render(
      <>
        <Combo />
        <button type="button">Elsewhere</button>
      </>,
    );

    fireEvent.click(trigger());
    act(() => screen.getByRole("button", { name: "Elsewhere" }).focus());

    expect(screen.queryByRole("listbox")).toBeNull();
  });

  it("picks with a click", () => {
    render(<Combo />);

    fireEvent.click(trigger());
    fireEvent.click(screen.getByRole("option", { name: "Linz" }));

    expect(screen.getByTestId("value").textContent).toBe("B5");
  });

  it("has no accessibility violations, open or closed", async () => {
    const { container } = render(<Combo />);
    await expectNoAxeViolations(container);

    fireEvent.click(trigger());

    await expectNoAxeViolations(container);
  });
});

describe("dates", () => {
  it.each([
    ["14.10.2026", "de", "2026-10-14"],
    ["1.2.2026", "de", "2026-02-01"],
    ["14/10/2026", "en", "2026-10-14"],
    ["2026-10-14", "en", "2026-10-14"],
    ["14.10.26", "de", "2026-10-14"],
    ["31.02.2026", "de", null],
    ["tomorrow", "en", null],
  ])("reads %s (%s) as %s", (text, locale, expected) => {
    expect(parseDate(text, locale)).toBe(expected);
  });

  it.each([
    ["de", "14.10.2026"],
    ["en", "14/10/2026"],
  ])("writes a date in %s as %s", (locale, expected) => {
    expect(formatDate("2026-10-14", locale)).toBe(expected);
  });

  it.each([
    ["9:30", "09:30"],
    ["0930", "09:30"],
    ["9.30", "09:30"],
    ["21", "21:00"],
    ["9:30 pm", "21:30"],
    ["12:15 am", "00:15"],
    ["24:00", null],
    ["9:75", null],
  ])("reads the time %s as %s", (text, expected) => {
    expect(parseTime(text)).toBe(expected);
  });
});

const dateTexts: DatePickerTexts = {
  openCalendar: "Choose a date",
  previousMonth: "Previous month",
  nextMonth: "Next month",
  invalid: "Enter a date like 14.10.2026.",
};

function Picker({ locale = "de", initial = null as string | null, min, max }: { locale?: string; initial?: string | null; min?: string; max?: string }) {
  const [value, setValue] = useState<string | null>(initial);

  return (
    <>
      <label htmlFor="started">Started on</label>
      <DatePicker id="started" locale={locale} value={value} onChange={setValue} texts={dateTexts} min={min} max={max} />
      <output data-testid="value">{value ?? "none"}</output>
    </>
  );
}

describe("DatePicker", () => {
  it("shows the date in the language's format and takes a typed date when leaving the field", () => {
    render(<Picker initial="2026-10-14" />);
    const input = screen.getByRole("textbox", { name: "Started on" });

    expect((input as HTMLInputElement).value).toBe("14.10.2026");
    fireEvent.change(input, { target: { value: "1.3.2027" } });
    fireEvent.blur(input);

    expect(screen.getByTestId("value").textContent).toBe("2027-03-01");
    expect((input as HTMLInputElement).value).toBe("01.03.2027");
  });

  it("explains a date it cannot read, at the field", () => {
    render(<Picker />);
    const input = screen.getByRole("textbox", { name: "Started on" });

    fireEvent.change(input, { target: { value: "31.02.2026" } });
    fireEvent.blur(input);

    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByText("Enter a date like 14.10.2026.")).toBeTruthy();
    expect(screen.getByTestId("value").textContent).toBe("none");
  });

  it("picks from the calendar: weeks start on Monday, arrows move by day and week, Enter picks", () => {
    render(<Picker initial="2026-10-14" />);

    fireEvent.click(screen.getByRole("button", { name: "Choose a date" }));
    const grid = screen.getByRole("grid");
    expect(within(grid).getAllByRole("columnheader")[0]?.getAttribute("abbr")).toBe("Montag");
    expect(document.activeElement).toBe(within(grid).getByRole("button", { name: "14. Oktober 2026" }));

    fireEvent.keyDown(document.activeElement!, { key: "ArrowDown" });
    fireEvent.keyDown(document.activeElement!, { key: "ArrowRight" });
    fireEvent.keyDown(document.activeElement!, { key: "Enter" });

    expect(screen.getByTestId("value").textContent).toBe("2026-10-22");
    expect(screen.queryByRole("grid")).toBeNull();
  });

  it("changes the month with the buttons and Page keys", () => {
    render(<Picker initial="2026-10-14" locale="en" />);

    fireEvent.click(screen.getByRole("button", { name: "Choose a date" }));
    fireEvent.click(screen.getByRole("button", { name: "Next month" }));

    expect(screen.getByRole("grid", { name: "November 2026" })).toBeTruthy();
    fireEvent.keyDown(document.activeElement!, { key: "PageUp" });
    expect(screen.getByRole("grid", { name: "October 2026" })).toBeTruthy();
  });

  it("does not offer days outside the allowed range", () => {
    render(<Picker initial="2026-10-14" max="2026-10-15" />);

    fireEvent.click(screen.getByRole("button", { name: "Choose a date" }));

    expect(screen.getByRole("button", { name: "16. Oktober 2026" }).hasAttribute("disabled")).toBe(true);
  });

  it("closes the calendar on a click outside", () => {
    render(<Picker initial="2026-10-14" />);

    fireEvent.click(screen.getByRole("button", { name: "Choose a date" }));
    fireEvent.mouseDown(document.body);

    expect(screen.queryByRole("grid")).toBeNull();
  });

  it("closes with Escape and gives the focus back", () => {
    render(<Picker initial="2026-10-14" />);
    const open = screen.getByRole("button", { name: "Choose a date" });

    fireEvent.click(open);
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });

    expect(screen.queryByRole("grid")).toBeNull();
    expect(document.activeElement).toBe(open);
  });

  it("has no accessibility violations, open or closed", async () => {
    const { container } = render(<Picker initial="2026-10-14" />);
    await expectNoAxeViolations(container);

    fireEvent.click(screen.getByRole("button", { name: "Choose a date" }));

    await expectNoAxeViolations(container);
  });
});

function Time({ initial = null as string | null }) {
  const [value, setValue] = useState<string | null>(initial);

  return (
    <>
      <label htmlFor="at">At</label>
      <TimeField id="at" value={value} onChange={setValue} minuteStep={15} texts={{ chooseTime: "Choose a time", invalid: "Enter a time like 09:30." }} />
      <output data-testid="value">{value ?? "none"}</output>
    </>
  );
}

describe("TimeField", () => {
  it("takes a typed time on the 24-hour clock", () => {
    render(<Time />);
    const input = screen.getByRole("textbox", { name: "At" });

    fireEvent.change(input, { target: { value: "930" } });
    fireEvent.blur(input);

    expect(screen.getByTestId("value").textContent).toBe("09:30");
    expect((input as HTMLInputElement).value).toBe("09:30");
  });

  it("offers the times in minute steps", () => {
    render(<Time initial="09:30" />);

    fireEvent.click(screen.getByRole("button", { name: "Choose a time" }));
    const options = within(screen.getByRole("listbox")).getAllByRole("option");

    expect(options).toHaveLength(96);
    expect(screen.getByRole("option", { name: "09:30" }).getAttribute("aria-selected")).toBe("true");
    fireEvent.click(screen.getByRole("option", { name: "17:45" }));
    expect(screen.getByTestId("value").textContent).toBe("17:45");
  });

  it("closes the list on a click outside and on Escape", () => {
    render(<Time initial="09:30" />);

    fireEvent.click(screen.getByRole("button", { name: "Choose a time" }));
    fireEvent.mouseDown(document.body);
    expect(screen.queryByRole("listbox")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Choose a time" }));
    fireEvent.keyDown(document.body, { key: "Escape" });
    expect(screen.queryByRole("listbox")).toBeNull();
  });

  it("explains a time it cannot read", () => {
    render(<Time />);
    const input = screen.getByRole("textbox", { name: "At" });

    fireEvent.change(input, { target: { value: "25:00" } });
    fireEvent.blur(input);

    expect(screen.getByText("Enter a time like 09:30.")).toBeTruthy();
  });

  it("has no accessibility violations", async () => {
    const { container } = render(<Time initial="09:30" />);

    await expectNoAxeViolations(container);
  });
});
