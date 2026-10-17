// SPDX-License-Identifier: AGPL-3.0-only
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DataGrid, dataGridTexts, pageItems, sortFromParam, sortToParam, type DataGridColumn, type DataGridSort } from "./index";
import { expectNoAxeViolations } from "./testing";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

type Survey = { id: string; title: string; status: string; responses: number };

const surveys: Survey[] = [
  { id: "s1", title: "Customer satisfaction 2026", status: "Published", responses: 1248 },
  { id: "s2", title: "Employee pulse Q4", status: "Draft", responses: 0 },
  { id: "s3", title: "Event feedback Linz", status: "Published", responses: 212 },
];

const columns: DataGridColumn<Survey>[] = [
  { key: "title", header: "Title", cell: (survey) => survey.title, sortable: true, phone: "title" },
  { key: "status", header: "Status", cell: (survey) => survey.status },
  { key: "responses", header: "Responses", cell: (survey) => survey.responses.toLocaleString("en"), align: "end", sortable: true },
];

const texts = dataGridTexts("en");

describe("pageItems", () => {
  it("lists every page when there are few", () => {
    expect(pageItems(2, 5)).toEqual([1, 2, 3, 4, 5]);
  });

  it("keeps the first, the last and the pages around the current one, with gaps", () => {
    expect(pageItems(2, 12)).toEqual([1, 2, 3, "gap", 12]);
    expect(pageItems(6, 12)).toEqual([1, "gap", 5, 6, 7, "gap", 12]);
    expect(pageItems(12, 12)).toEqual([1, "gap", 11, 12]);
  });
});

describe("sort parameters", () => {
  it("reads and writes a leading minus as descending", () => {
    expect(sortFromParam("-changed")).toEqual({ key: "changed", descending: true });
    expect(sortFromParam("title")).toEqual({ key: "title", descending: false });
    expect(sortToParam({ key: "changed", descending: true })).toBe("-changed");
  });
});

describe("dataGridTexts", () => {
  it("formats ranges and counts in the language", () => {
    expect(dataGridTexts("en").range(51, 100, 584)).toBe("51–100 of 584");
    expect(dataGridTexts("de").range(51, 100, 1248)).toBe("51–100 von 1.248");
    expect(dataGridTexts("de").loaded(150, 1248)).toBe("150 von 1.248 geladen");
  });
});

function Pages({ total = 584, initialPage = 2 }: { total?: number; initialPage?: number }) {
  const [page, setPage] = useState(initialPage);
  const [pageSize, setPageSize] = useState(50);
  const [sort, setSort] = useState<DataGridSort>({ key: "responses", descending: true });

  return (
    <>
      <DataGrid
        label="Surveys"
        columns={columns}
        rows={surveys}
        rowKey={(survey) => survey.id}
        sort={sort}
        onSort={setSort}
        paging={{ page, pageSize, total, onPage: setPage, onPageSize: setPageSize }}
        texts={texts}
      />
      <output data-testid="state">{`${page} ${pageSize} ${sortToParam(sort)}`}</output>
    </>
  );
}

describe("DataGrid", () => {
  it("shows a header and a row per item, numbers aligned to the end", () => {
    render(<Pages />);

    const grid = screen.getByRole("grid", { name: "Surveys" });
    expect(within(grid).getAllByRole("columnheader").map((header) => header.textContent)).toEqual(["Title", "Status", "Responses"]);
    expect(within(grid).getAllByRole("row")).toHaveLength(4);
    expect(within(grid).getAllByRole("gridcell")).toHaveLength(9);
    expect(within(grid).getByText("1,248").closest("td")!.className).toContain("aw-grid__cell--end");
    expect(within(grid).getByText("Employee pulse Q4").closest("td")!.getAttribute("data-label")).toBe("Title");
  });

  it("marks the sorted column and sorts by a header, flipping the direction on the same one", () => {
    render(<Pages />);

    const responses = screen.getByRole("columnheader", { name: /Responses/ });
    expect(responses.getAttribute("aria-sort")).toBe("descending");
    expect(screen.getByRole("columnheader", { name: "Status" }).getAttribute("aria-sort")).toBeNull();
    expect(within(screen.getByRole("columnheader", { name: "Status" })).queryByRole("button")).toBeNull();

    fireEvent.click(within(responses).getByRole("button"));
    expect(screen.getByTestId("state").textContent).toBe("1 50 responses");

    fireEvent.click(within(screen.getByRole("columnheader", { name: /Title/ })).getByRole("button"));
    expect(screen.getByTestId("state").textContent).toBe("1 50 title");
  });

  it("pages with numbers and gaps, previous and next", () => {
    render(<Pages />);

    expect(screen.getByText("51–100 of 584")).toBeTruthy();
    const pager = screen.getByRole("navigation", { name: "Pages" });
    expect(within(pager).getAllByRole("button").map((button) => button.textContent)).toEqual(["‹", "1", "2", "3", "…", "12", "›"].filter((text) => text !== "…"));
    expect(within(pager).getByRole("button", { name: "Page 2" }).getAttribute("aria-current")).toBe("page");

    fireEvent.click(within(pager).getByRole("button", { name: "Page 12" }));
    expect(screen.getByTestId("state").textContent).toBe("12 50 -responses");
    expect((within(pager).getByRole("button", { name: "Next page" }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(within(pager).getByRole("button", { name: "Previous page" }));
    expect(screen.getByTestId("state").textContent).toBe("11 50 -responses");
  });

  it("changes the page size and goes back to the first page", () => {
    render(<Pages />);

    fireEvent.click(screen.getByRole("button", { name: /50 per page/ }));
    fireEvent.click(screen.getByRole("option", { name: "100 per page" }));

    expect(screen.getByTestId("state").textContent).toBe("1 100 -responses");
  });

  it("hides the pager when everything fits on one page", () => {
    render(<Pages total={3} initialPage={1} />);

    expect(screen.getByText("1–3 of 3")).toBeTruthy();
    expect(screen.queryByRole("navigation", { name: "Pages" })).toBeNull();
  });

  it("opens a row by clicking it", () => {
    const opened: string[] = [];
    render(<DataGrid label="Surveys" columns={columns} rows={surveys} rowKey={(survey) => survey.id} onOpen={(survey) => opened.push(survey.id)} texts={texts} />);

    fireEvent.click(screen.getByText("Event feedback Linz"));

    expect(opened).toEqual(["s3"]);
  });

  it("shows skeleton rows while the first page loads", () => {
    render(<DataGrid label="Surveys" columns={columns} rows={null} rowKey={(survey) => survey.id} texts={texts} />);

    const grid = screen.getByRole("grid", { name: "Surveys" });
    expect(grid.getAttribute("aria-busy")).toBe("true");
    expect(grid.querySelectorAll("tbody tr")).toHaveLength(5);
    expect(grid.querySelectorAll(".aw-skeleton").length).toBeGreaterThan(0);
  });

  it("shows the empty state instead of the grid when there are no rows", () => {
    render(<DataGrid label="Surveys" columns={columns} rows={[]} rowKey={(survey) => survey.id} empty={<p>No surveys yet.</p>} texts={texts} />);

    expect(screen.getByText("No surveys yet.")).toBeTruthy();
    expect(screen.queryByRole("grid")).toBeNull();
  });

  it("says when loading failed and tries again", () => {
    let retried = 0;
    render(<DataGrid label="Surveys" columns={columns} rows={null} rowKey={(survey) => survey.id} error onRetry={() => retried++} texts={texts} />);

    expect(screen.getByRole("alert").textContent).toContain("Not loaded. The server did not answer.");
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(retried).toBe(1);
  });

  it("has no axe violations with paging and sorting", async () => {
    const { container } = render(<Pages />);

    await expectNoAxeViolations(container);
  });
});

function Chooser({ total = 640 }: { total?: number }) {
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [all, setAll] = useState(false);
  const [opened, setOpened] = useState<string[]>([]);

  return (
    <>
      <DataGrid
        label="Participants"
        columns={columns}
        rows={surveys}
        rowKey={(survey) => survey.id}
        onOpen={(survey) => setOpened([...opened, survey.id])}
        paging={{ page: 1, pageSize: 50, total, onPage: () => undefined, onPageSize: () => undefined }}
        selection={{
          selected,
          all,
          rowLabel: (survey) => survey.title,
          onChange: (next, nextAll) => {
            setSelected(next);
            setAll(nextAll);
          },
          actions: <button type="button">Send reminder</button>,
        }}
        texts={texts}
      />
      <output data-testid="chosen">{all ? "all" : [...selected].join(" ")}</output>
      <output data-testid="opened">{opened.join(" ")}</output>
    </>
  );
}

describe("DataGrid selection", () => {
  const box = (name: string) => screen.getByRole("checkbox", { name }) as HTMLInputElement;

  it("selects rows by their box without opening them, and shows the actions for them", () => {
    render(<Chooser />);

    expect(screen.queryByRole("region", { name: /selected/ })).toBeNull();
    fireEvent.click(box("Select: Employee pulse Q4"));
    fireEvent.click(box("Select: Event feedback Linz"));

    expect(screen.getByTestId("chosen").textContent).toBe("s2 s3");
    expect(screen.getByTestId("opened").textContent).toBe("");
    const bar = screen.getByRole("region", { name: "2 selected" });
    expect(within(bar).getByRole("button", { name: "Send reminder" })).toBeTruthy();
    expect(box("Select this page").indeterminate).toBe(true);

    fireEvent.click(within(bar).getByRole("button", { name: "Clear" }));
    expect(screen.getByTestId("chosen").textContent).toBe("");
  });

  it("W35: a boxed bar says the count in its own words and what it is of, and its box clears the choice; the compact pager sits in the card", () => {
    const onChange = vi.fn();
    const onPage = vi.fn();
    const { container } = render(
      <DataGrid
        label="Participants"
        columns={columns}
        rows={surveys}
        rowKey={(survey) => survey.id}
        paging={{ page: 1, pageSize: 50, total: 86, of: 640, note: "Sales: 86", compact: true, onPage, onPageSize: () => undefined }}
        selection={{ selected: new Set(["s1"]), all: false, rowLabel: (survey) => survey.title, onChange, actions: <button type="button">Remind now</button>, count: (count) => `${count} chosen`, context: "· of 392 not answered", boxed: true }}
        texts={texts}
      />,
    );
    const bar = screen.getByRole("region", { name: "1 chosen" });

    expect(bar.textContent).toBe("1 chosen· of 392 not answeredRemind now");
    expect(within(bar).queryByRole("button", { name: "Clear" })).toBeNull();
    fireEvent.click(within(bar).getByRole("checkbox", { name: "Clear" }));
    expect(onChange).toHaveBeenCalledWith(new Set(), false);
    const card = container.querySelector(".aw-grid__card")!;
    expect(within(card as HTMLElement).getByText("1–50 of 640 · Sales: 86")).toBeTruthy();
    expect(within(card as HTMLElement).queryByRole("button", { name: "Page 2" })).toBeNull();
    fireEvent.click(within(card as HTMLElement).getByRole("button", { name: "Next page" }));
    expect(onPage).toHaveBeenCalledWith(2);
  });

  it("selects the page by the header box, then every match across pages", () => {
    render(<Chooser />);

    fireEvent.click(box("Select this page"));
    expect(screen.getByTestId("chosen").textContent).toBe("s1 s2 s3");
    expect(screen.getByText("All 3 on this page are selected.")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Select all 640" }));
    expect(screen.getByTestId("chosen").textContent).toBe("all");
    expect(screen.getByRole("region", { name: "640 selected" })).toBeTruthy();
    expect(screen.getByText("All 640 selected.")).toBeTruthy();
    expect(box("Select: Customer satisfaction 2026").checked).toBe(true);

    fireEvent.click(box("Select: Customer satisfaction 2026"));
    expect(screen.getByTestId("chosen").textContent).toBe("s2 s3");
  });

  it("offers the page's box only once its rows are there", () => {
    render(
      <DataGrid
        label="Participants"
        columns={columns}
        rows={null}
        rowKey={(survey) => survey.id}
        selection={{ selected: new Set(), all: false, rowLabel: (survey) => survey.title, onChange: () => undefined, actions: null }}
        texts={texts}
      />,
    );

    expect(screen.queryByRole("checkbox", { name: "Select this page" })).toBeNull();
  });

  it("offers no select-all when the page holds everything", () => {
    render(<Chooser total={3} />);

    fireEvent.click(box("Select this page"));

    expect(screen.queryByRole("button", { name: /Select all/ })).toBeNull();
  });

  it("has no axe violations with rows selected", async () => {
    const { container } = render(<Chooser />);
    fireEvent.click(box("Select this page"));

    await expectNoAxeViolations(container);
  });
});

type Answer = { id: string; started: string };

const answers = (count: number): Answer[] => Array.from({ length: count }, (_, index) => ({ id: `r${index + 1}`, started: `Response ${index + 1}` }));

const answerColumns: DataGridColumn<Answer>[] = [{ key: "started", header: "Started", cell: (answer) => answer.started, align: "end", sortable: true }];

/** Records what the grid observes and lets a test say the end came into view. */
class FakeObserver {
  static instances: FakeObserver[] = [];
  constructor(private readonly callback: IntersectionObserverCallback) {
    FakeObserver.instances.push(this);
  }
  observe() {}
  disconnect() {}
  show() {
    this.callback([{ isIntersecting: true } as IntersectionObserverEntry], this as unknown as IntersectionObserver);
  }
}

describe("DataGrid loading more", () => {
  it("loads more by the button, and says how many are loaded", () => {
    let asked = 0;
    render(<DataGrid label="Responses" columns={answerColumns} rows={answers(150)} rowKey={(answer) => answer.id} more={{ total: 1248, loading: false, onMore: () => asked++ }} texts={dataGridTexts("de")} />);

    expect(screen.getByText("150 von 1.248 geladen")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Weitere laden" }));

    expect(asked).toBe(1);
  });

  it("loads more when the end of the list comes into view, but not while loading", () => {
    FakeObserver.instances = [];
    vi.stubGlobal("IntersectionObserver", FakeObserver);
    let asked = 0;
    const { rerender } = render(<DataGrid label="Responses" columns={answerColumns} rows={answers(50)} rowKey={(answer) => answer.id} more={{ total: 120, loading: false, onMore: () => asked++ }} texts={texts} />);

    FakeObserver.instances.at(-1)!.show();
    rerender(<DataGrid label="Responses" columns={answerColumns} rows={answers(50)} rowKey={(answer) => answer.id} more={{ total: 120, loading: true, onMore: () => asked++ }} texts={texts} />);
    FakeObserver.instances.at(-1)!.show();

    expect(asked).toBe(1);
    expect(screen.getByText("Loading more …")).toBeTruthy();
    expect(screen.getByRole("grid").querySelectorAll("tbody .aw-skeleton").length).toBeGreaterThan(0);
  });

  it("says when everything is loaded", () => {
    render(<DataGrid label="Responses" columns={answerColumns} rows={answers(3)} rowKey={(answer) => answer.id} more={{ total: 3, loading: false, onMore: () => undefined }} texts={texts} />);

    expect(screen.getByText("All 3 loaded.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
  });

  it("keeps only the rows near the screen in the page, and the rest as space", () => {
    render(<DataGrid label="Responses" columns={answerColumns} rows={answers(1000)} rowKey={(answer) => answer.id} more={{ total: 5000, loading: false, onMore: () => undefined }} texts={texts} />);
    const grid = screen.getByRole("grid");
    const scroller = grid.closest(".aw-grid__scroll")!;

    expect(grid.querySelectorAll("tbody tr.aw-grid__row").length).toBeLessThan(100);
    expect(grid.getAttribute("aria-rowcount")).toBe("5001");

    scroller.scrollTop = 40 * 600;
    fireEvent.scroll(scroller);

    expect(screen.getByText("Response 601")).toBeTruthy();
    expect(screen.queryByText("Response 1")).toBeNull();
    expect(screen.getByText("Response 601").closest("tr")!.getAttribute("aria-rowindex")).toBe("602");
  });
});

describe("DataGrid keyboard and phones", () => {
  it("moves between rows with the arrows, Home and End, and opens with Enter", () => {
    const opened: string[] = [];
    render(<DataGrid label="Surveys" columns={columns} rows={surveys} rowKey={(survey) => survey.id} onOpen={(survey) => opened.push(survey.id)} texts={texts} />);
    const rows = () => screen.getAllByRole("row").slice(1);

    expect(rows().map((row) => row.tabIndex)).toEqual([0, -1, -1]);
    rows()[0]!.focus();
    fireEvent.keyDown(rows()[0]!, { key: "ArrowDown" });
    expect(document.activeElement).toBe(rows()[1]);
    fireEvent.keyDown(rows()[1]!, { key: "End" });
    expect(document.activeElement).toBe(rows()[2]);
    fireEvent.keyDown(rows()[2]!, { key: "Home" });
    expect(document.activeElement).toBe(rows()[0]);
    fireEvent.keyDown(rows()[0]!, { key: "Enter" });

    expect(opened).toEqual(["s1"]);
    expect(rows()[0]!.tabIndex).toBe(0);
  });

  it("marks the opened row", () => {
    render(<DataGrid label="Surveys" columns={columns} rows={surveys} rowKey={(survey) => survey.id} onOpen={() => undefined} active="s2" texts={texts} />);

    expect(screen.getByText("Employee pulse Q4").closest("tr")!.getAttribute("aria-current")).toBe("true");
  });

  it("offers one Sort menu for phones, where there are no headers to click", () => {
    render(<Pages />);

    fireEvent.click(screen.getByRole("button", { name: /^Sort/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Title ↑", "Title ↓", "Responses ↑", "Responses ↓"]);
    fireEvent.click(screen.getByRole("option", { name: "Title ↓" }));

    expect(screen.getByTestId("state").textContent).toBe("1 50 -title");
  });
});
