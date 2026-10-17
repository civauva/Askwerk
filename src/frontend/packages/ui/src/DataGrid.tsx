// SPDX-License-Identifier: AGPL-3.0-only
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { ComboBox } from "./ComboBox";
import { Skeleton } from "./Skeleton";

/** A column of the grid. Numbers, durations and dates align to the end and use the mono face. */
export type DataGridColumn<Row> = {
  readonly key: string;
  readonly header: string;
  readonly cell: (row: Row) => ReactNode;
  readonly align?: "start" | "end";
  /** The server sorts by this column. */
  readonly sortable?: boolean;
  /** On phones: the card's title, or left out; otherwise label and value. */
  readonly phone?: "title" | "hidden";
  /** The header is for screen readers only (W35: the column of row actions shows no header). */
  readonly hideHeader?: boolean;
};

export type DataGridSort = { readonly key: string; readonly descending: boolean };

/** Paging (W13 A, the default): numbered pages of 25, 50 or 100 rows. */
export type DataGridPaging = {
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly onPage: (page: number) => void;
  readonly onPageSize: (pageSize: number) => void;
  /**
   * W35: the pager inside the grid's card, the range and ‹ › only (no page numbers, no page size). "of" may name
   * another number than the pages count (all, while a filter is on), and a note follows the range.
   */
  readonly compact?: boolean;
  readonly of?: number;
  readonly note?: string;
};

/**
 * Choosing rows (W13 C), only where a screen does something with several rows. all means every row that matches,
 * across pages; selected then is ignored.
 */
export type DataGridSelection<Row> = {
  readonly selected: ReadonlySet<string>;
  readonly all: boolean;
  readonly onChange: (selected: ReadonlySet<string>, all: boolean) => void;
  /** Names a row's box for screen readers. */
  readonly rowLabel: (row: Row) => string;
  /** What the screen does with the chosen rows, shown in the bar. */
  readonly actions: ReactNode;
  /** The count in the screen's own words ("3 chosen"); texts.selected otherwise. */
  readonly count?: (count: number) => string;
  /** Says more after the count, dimmed (W35: "· of 392 not answered"). */
  readonly context?: ReactNode;
  /** W35: a white box at the start clears the choice, the actions sit at the end as small outlined buttons, no Clear. */
  readonly boxed?: boolean;
};

export type DataGridTexts = {
  readonly range: (from: number, to: number, total: number) => string;
  readonly perPage: (size: number) => string;
  readonly rowsPerPage: string;
  readonly pages: string;
  readonly page: (page: number) => string;
  readonly previous: string;
  readonly next: string;
  readonly loaded: (shown: number, total: number) => string;
  readonly allLoaded: (total: number) => string;
  readonly loadMore: string;
  readonly loadingMore: string;
  readonly selected: (count: number) => string;
  readonly selectPage: string;
  readonly selectRow: string;
  readonly pageSelected: (count: number) => string;
  readonly selectAll: (total: number) => string;
  readonly allSelected: (total: number) => string;
  readonly clear: string;
  readonly sort: string;
  readonly sortedBy: (header: string, descending: boolean) => string;
  readonly failed: string;
  readonly retry: string;
};

/** Askwerk's texts for the grid in German and English. */
export function dataGridTexts(locale: "de" | "en"): DataGridTexts {
  const count = new Intl.NumberFormat(locale === "de" ? "de-DE" : "en-GB");
  const n = (value: number) => count.format(value);

  return locale === "de"
    ? {
        range: (from, to, total) => `${n(from)}–${n(to)} von ${n(total)}`,
        perPage: (size) => `${n(size)} pro Seite`,
        rowsPerPage: "Zeilen pro Seite",
        pages: "Seiten",
        page: (page) => `Seite ${n(page)}`,
        previous: "Vorherige Seite",
        next: "Nächste Seite",
        loaded: (shown, total) => `${n(shown)} von ${n(total)} geladen`,
        allLoaded: (total) => `Alle ${n(total)} geladen.`,
        loadMore: "Weitere laden",
        loadingMore: "Lädt weitere …",
        selected: (selected) => `${n(selected)} ausgewählt`,
        selectPage: "Diese Seite auswählen",
        selectRow: "Auswählen",
        pageSelected: (selected) => `Alle ${n(selected)} auf dieser Seite sind ausgewählt.`,
        selectAll: (total) => `Alle ${n(total)} auswählen`,
        allSelected: (total) => `Alle ${n(total)} ausgewählt.`,
        clear: "Auswahl aufheben",
        sort: "Sortieren",
        sortedBy: (header, descending) => `${header} ${descending ? "↓" : "↑"}`,
        failed: "Nicht geladen. Der Server hat nicht geantwortet.",
        retry: "Erneut versuchen",
      }
    : {
        range: (from, to, total) => `${n(from)}–${n(to)} of ${n(total)}`,
        perPage: (size) => `${n(size)} per page`,
        rowsPerPage: "Rows per page",
        pages: "Pages",
        page: (page) => `Page ${n(page)}`,
        previous: "Previous page",
        next: "Next page",
        loaded: (shown, total) => `${n(shown)} of ${n(total)} loaded`,
        allLoaded: (total) => `All ${n(total)} loaded.`,
        loadMore: "Load more",
        loadingMore: "Loading more …",
        selected: (selected) => `${n(selected)} selected`,
        selectPage: "Select this page",
        selectRow: "Select",
        pageSelected: (selected) => `All ${n(selected)} on this page are selected.`,
        selectAll: (total) => `Select all ${n(total)}`,
        allSelected: (total) => `All ${n(total)} selected.`,
        clear: "Clear",
        sort: "Sort",
        sortedBy: (header, descending) => `${header} ${descending ? "↓" : "↑"}`,
        failed: "Not loaded. The server did not answer.",
        retry: "Try again",
      };
}

/** The page buttons to show: all of few pages, else the first, the last and the neighbours of the current one. */
export function pageItems(current: number, count: number): (number | "gap")[] {
  if (count <= 7) {
    return Array.from({ length: count }, (_, index) => index + 1);
  }

  const items: (number | "gap")[] = [1];
  const from = Math.max(2, current - 1);
  const to = Math.min(count - 1, current + 1);

  if (from > 2) {
    items.push("gap");
  }

  for (let page = from; page <= to; page++) {
    items.push(page);
  }

  if (to < count - 1) {
    items.push("gap");
  }

  items.push(count);

  return items;
}

/** "-changed" is changed, descending; the form the App API takes and the address keeps. */
export function sortFromParam(param: string): DataGridSort {
  return param.startsWith("-") ? { key: param.slice(1), descending: true } : { key: param, descending: false };
}

export function sortToParam(sort: DataGridSort): string {
  return sort.descending ? `-${sort.key}` : sort.key;
}

export const pageSizes = [25, 50, 100] as const;

/** Loading more as the list scrolls (W13 B, the response list): rows holds everything loaded so far. */
export type DataGridMore = {
  readonly total: number;
  readonly loading: boolean;
  readonly onMore: () => void;
};

type DataGridProps<Row> = {
  /** The grid's accessible name. */
  label: string;
  columns: readonly DataGridColumn<Row>[];
  /** null while the first rows load. */
  rows: readonly Row[] | null;
  rowKey: (row: Row) => string;
  sort?: DataGridSort | undefined;
  onSort?: (sort: DataGridSort) => void;
  /** Makes the whole row a target; Enter opens the focused row. */
  onOpen?: (row: Row) => void;
  /** The key of the opened row. */
  active?: string | null;
  paging?: DataGridPaging;
  more?: DataGridMore;
  selection?: DataGridSelection<Row>;
  /** Keeps the first column in place while scrolling sideways. */
  stickyFirst?: boolean;
  /** Shown instead of the grid when there are no rows. */
  empty?: ReactNode;
  error?: boolean;
  onRetry?: () => void;
  texts: DataGridTexts;
};

const skeletonRows = 5;
const moreSkeletonRows = 3;
/** Rows are 40 px high (W13), so a window of rows can be worked out from the scroll position. */
const rowHeight = 40;
/** From this many loaded rows on, only those near the screen stay in the page. */
const windowFrom = 200;
const overscan = 20;

/**
 * The one data grid of Askwerk (AWK-19, W13): sortable headers, numbers aligned to the end, paging with page numbers
 * or loading more while scrolling, choosing rows, keys to move and open, skeleton rows while loading, and a card per
 * row on phones (CSS, from each cell's data-label) with one Sort menu.
 */
export function DataGrid<Row>({
  label,
  columns,
  rows,
  rowKey,
  sort,
  onSort,
  onOpen,
  active = null,
  paging,
  more,
  selection,
  stickyFirst = false,
  empty,
  error = false,
  onRetry,
  texts,
}: DataGridProps<Row>) {
  const id = useId();
  const keys = rows?.map(rowKey) ?? [];
  const scroller = useRef<HTMLDivElement>(null);
  const end = useRef<HTMLDivElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [focused, setFocused] = useState(0);
  const pendingFocus = useRef(false);
  const latestMore = useRef(more);
  latestMore.current = more;
  const loaded = rows?.length ?? 0;
  const hasMore = more !== undefined;
  const moreLoading = more?.loading ?? false;

  // Loads more when the end of the list comes into view; observed again after each load, in case it still shows.
  useEffect(() => {
    if (!hasMore || end.current === null || typeof IntersectionObserver === "undefined") {
      return;
    }

    const observer = new IntersectionObserver((entries) => {
      const current = latestMore.current;

      if (entries.some((entry) => entry.isIntersecting) && current !== undefined && !current.loading && loaded < current.total) {
        current.onMore();
      }
    });
    observer.observe(end.current);

    return () => observer.disconnect();
  }, [hasMore, loaded, moreLoading]);

  const windowed = rows !== null && rows.length > windowFrom;
  const viewport = scroller.current?.clientHeight || 800;
  const first = windowed ? Math.max(0, Math.floor(scrollTop / rowHeight) - overscan) : 0;
  const last = windowed ? Math.min(rows.length, Math.ceil((scrollTop + viewport) / rowHeight) + overscan) : loaded;

  useEffect(() => {
    if (!pendingFocus.current) {
      return;
    }

    pendingFocus.current = false;
    const row = scroller.current?.querySelector<HTMLElement>(`tr[data-index="${focused}"]`);
    row?.focus();
    row?.scrollIntoView?.({ block: "nearest" });
  }, [focused, first]);

  if (error && (rows === null || rows.length === 0)) {
    return (
      <div className="aw-grid aw-grid--message" role="alert">
        <p>{texts.failed}</p>
        {onRetry !== undefined && (
          <button type="button" className="aw-button aw-button--quiet" onClick={onRetry}>
            {texts.retry}
          </button>
        )}
      </div>
    );
  }

  if (rows !== null && rows.length === 0 && empty !== undefined) {
    return <div className="aw-grid aw-grid--message">{empty}</div>;
  }

  const cellClass = (column: DataGridColumn<Row>) =>
    ["aw-grid__cell", column.align === "end" && "aw-grid__cell--end", column.phone !== undefined && `aw-grid__cell--${column.phone}`].filter(Boolean).join(" ");

  const isChosen = (key: string) => selection !== undefined && (selection.all || selection.selected.has(key));
  const chosenOnPage = keys.filter(isChosen).length;
  const total = paging?.total ?? more?.total ?? keys.length;
  const sortable = columns.filter((column) => column.sortable);
  const columnCount = columns.length + (selection !== undefined ? 1 : 0);
  const shown = rows === null ? [] : rows.slice(first, last);

  function choose(key: string) {
    if (selection === undefined) {
      return;
    }

    const next = new Set(selection.all ? keys : selection.selected);

    if (isChosen(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }

    selection.onChange(next, false);
  }

  function sortBy(next: DataGridSort) {
    onSort?.(next);
    paging?.onPage(1);
  }

  function moveTo(index: number) {
    const target = Math.max(0, Math.min(loaded - 1, index));

    if (windowed && (target < first || target >= last) && scroller.current !== null) {
      scroller.current.scrollTop = target * rowHeight;
      setScrollTop(target * rowHeight);
    }

    pendingFocus.current = true;
    setFocused(target);
  }

  function onRowKey(event: KeyboardEvent<HTMLTableRowElement>, index: number, row: Row) {
    const step = Math.max(1, Math.floor(viewport / rowHeight) - 1);
    const moves: Record<string, number> = { ArrowDown: index + 1, ArrowUp: index - 1, Home: 0, End: loaded - 1, PageDown: index + step, PageUp: index - step };
    const onRow = event.target === event.currentTarget;

    if (event.key in moves) {
      event.preventDefault();
      moveTo(moves[event.key]!);
    } else if (onRow && event.key === "Enter" && onOpen !== undefined) {
      event.preventDefault();
      onOpen(row);
    } else if (onRow && event.key === " " && selection !== undefined) {
      event.preventDefault();
      choose(rowKey(row));
    }
  }

  return (
    <div className={stickyFirst ? "aw-grid aw-grid--sticky" : "aw-grid"}>
      {selection !== undefined && (selection.all || selection.selected.size > 0) && (
        <div role="region" className={selection.boxed ? "aw-grid__bar aw-grid__bar--boxed" : "aw-grid__bar"} aria-label={(selection.count ?? texts.selected)(selection.all ? total : selection.selected.size)}>
          {selection.boxed && <input type="checkbox" className="aw-grid__bar-box" aria-label={texts.clear} checked onChange={() => selection.onChange(new Set(), false)} />}
          <span className="aw-grid__bar-count">{(selection.count ?? texts.selected)(selection.all ? total : selection.selected.size)}</span>
          {selection.context !== undefined && <span className="aw-grid__bar-context">{selection.context}</span>}
          {selection.boxed ? (
            <span className="aw-grid__bar-actions">{selection.actions}</span>
          ) : (
            <>
              {selection.actions}
              <button type="button" className="aw-grid__bar-clear" onClick={() => selection.onChange(new Set(), false)}>
                {texts.clear}
              </button>
            </>
          )}
        </div>
      )}
      {selection !== undefined && keys.length > 0 && chosenOnPage === keys.length && total > keys.length && (
        <p className="aw-grid__all">
          {selection.all ? (
            texts.allSelected(total)
          ) : (
            <>
              {texts.pageSelected(keys.length)}{" "}
              <button type="button" className="aw-grid__link" onClick={() => selection.onChange(new Set(), true)}>
                {texts.selectAll(total)}
              </button>
            </>
          )}
        </p>
      )}
      {sortable.length > 0 && onSort !== undefined && (
        <div className="aw-grid__phone-sort">
          <span id={`${id}-sort`} className="aw-grid__phone-sort-label">
            {texts.sort}
          </span>
          <ComboBox
            labelledBy={`${id}-sort`}
            options={sortable.flatMap((column) => [false, true].map((descending) => ({ value: sortToParam({ key: column.key, descending }), label: texts.sortedBy(column.header, descending) })))}
            value={sort === undefined ? null : sortToParam(sort)}
            onChange={(value) => sortBy(sortFromParam(value))}
            texts={{ choose: texts.sort }}
          />
        </div>
      )}
      <GridCard inside={paging?.compact === true} footer={paging !== undefined && rows !== null && <Pager id={id} paging={paging} texts={texts} />}>
      <div
        ref={scroller}
        className={windowed ? "aw-grid__scroll aw-grid__scroll--window" : "aw-grid__scroll"}
        onScroll={windowed ? (event) => setScrollTop(event.currentTarget.scrollTop) : undefined}
      >
        <table
          role="grid"
          className="aw-grid__table"
          aria-label={label}
          aria-busy={rows === null || moreLoading || undefined}
          aria-rowcount={paging !== undefined || more !== undefined ? total + 1 : undefined}
        >
          <thead>
            <tr aria-rowindex={1}>
              {selection !== undefined && (
                <th scope="col" className="aw-grid__cell aw-grid__cell--check">
                  {rows !== null && (
                  <Checkbox
                    label={texts.selectPage}
                    checked={keys.length > 0 && chosenOnPage === keys.length}
                    mixed={chosenOnPage > 0 && chosenOnPage < keys.length}
                    onChange={(checked) => selection.onChange(checked ? new Set([...selection.selected, ...keys]) : new Set(), false)}
                  />
                  )}
                </th>
              )}
              {columns.map((column) => {
                const sorted = sort?.key === column.key;

                return (
                  <th key={column.key} scope="col" className={cellClass(column)} aria-sort={sorted ? (sort.descending ? "descending" : "ascending") : undefined}>
                    {column.sortable && onSort !== undefined ? (
                      <button
                        type="button"
                        className="aw-grid__sort-button"
                        data-sort={sorted ? (sort.descending ? "descending" : "ascending") : "none"}
                        onClick={() => sortBy({ key: column.key, descending: sorted ? !sort.descending : false })}
                      >
                        {column.header}
                      </button>
                    ) : column.hideHeader ? (
                      <span className="aw-visually-hidden">{column.header}</span>
                    ) : (
                      column.header
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {rows === null && <SkeletonRows count={skeletonRows} columns={columns} check={selection !== undefined} cellClass={cellClass} />}
            {first > 0 && (
              <tr aria-hidden="true" className="aw-grid__space">
                <td colSpan={columnCount} style={{ height: first * rowHeight }} />
              </tr>
            )}
            {shown.map((row, offset) => {
              const index = first + offset;
              const key = rowKey(row);

              return (
                <tr
                  key={key}
                  data-index={index}
                  aria-rowindex={(paging !== undefined ? (paging.page - 1) * paging.pageSize : 0) + index + 2}
                  tabIndex={index === Math.min(focused, loaded - 1) ? 0 : -1}
                  className={onOpen !== undefined ? "aw-grid__row aw-grid__row--open" : "aw-grid__row"}
                  aria-selected={selection !== undefined ? isChosen(key) : undefined}
                  aria-current={key === active ? "true" : undefined}
                  onClick={onOpen === undefined ? undefined : () => onOpen(row)}
                  onFocus={(event) => event.target === event.currentTarget && setFocused(index)}
                  onKeyDown={(event) => onRowKey(event, index, row)}
                >
                  {selection !== undefined && (
                    <td role="gridcell" className="aw-grid__cell aw-grid__cell--check" onClick={(event) => event.stopPropagation()}>
                      <Checkbox label={`${texts.selectRow}: ${selection.rowLabel(row)}`} checked={isChosen(key)} onChange={() => choose(key)} />
                    </td>
                  )}
                  {columns.map((column) => (
                    <td key={column.key} role="gridcell" className={cellClass(column)} data-label={column.header}>
                      {column.cell(row)}
                    </td>
                  ))}
                </tr>
              );
            })}
            {rows !== null && last < rows.length && (
              <tr aria-hidden="true" className="aw-grid__space">
                <td colSpan={columnCount} style={{ height: (rows.length - last) * rowHeight }} />
              </tr>
            )}
            {moreLoading && <SkeletonRows count={moreSkeletonRows} columns={columns} check={selection !== undefined} cellClass={cellClass} />}
          </tbody>
        </table>
        {more !== undefined && <div ref={end} className="aw-grid__end" />}
      </div>
      </GridCard>
      {more !== undefined && rows !== null && (
        <div className="aw-grid__footer">
          <span className="aw-grid__range" aria-live="polite">
            {moreLoading ? texts.loadingMore : loaded >= more.total ? texts.allLoaded(more.total) : texts.loaded(loaded, more.total)}
          </span>
          {loaded < more.total && (
            <button type="button" className="aw-button aw-button--quiet" disabled={moreLoading} onClick={more.onMore}>
              {texts.loadMore}
            </button>
          )}
        </div>
      )}
    </div>
  );
}

function SkeletonRows<Row>({ count, columns, check, cellClass }: { count: number; columns: readonly DataGridColumn<Row>[]; check: boolean; cellClass: (column: DataGridColumn<Row>) => string }) {
  return Array.from({ length: count }, (_, index) => (
    <tr key={`skeleton-${index}`} aria-hidden="true" className="aw-grid__skeleton">
      {check && <td className="aw-grid__cell aw-grid__cell--check" />}
      {columns.map((column) => (
        <td key={column.key} className={cellClass(column)}>
          <Skeleton width={`${40 + ((index * 23 + column.key.length * 11) % 50)}%`} height="10px" />
        </td>
      ))}
    </tr>
  ));
}

/** A box that can show "some" (indeterminate), which React cannot set as an attribute. */
function Checkbox({ label, checked, mixed = false, onChange }: { label: string; checked: boolean; mixed?: boolean; onChange: (checked: boolean) => void }) {
  const box = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (box.current !== null) {
      box.current.indeterminate = mixed;
    }
  }, [mixed]);

  return <input ref={box} type="checkbox" className="aw-grid__check" aria-label={label} checked={checked} onChange={(event) => onChange(event.target.checked)} />;
}

/** The table's card; with a compact pager (W35) the pager sits inside it, below the rows, else after it. */
function GridCard({ inside, footer, children }: { inside: boolean; footer: ReactNode; children: ReactNode }) {
  return inside ? (
    <div className="aw-grid__card">
      {children}
      {footer}
    </div>
  ) : (
    <>
      {children}
      {footer}
    </>
  );
}

function Pager({ id, paging, texts }: { id: string; paging: DataGridPaging; texts: DataGridTexts }) {
  const { page, pageSize, total, onPage, onPageSize } = paging;
  const count = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);

  if (paging.compact) {
    return (
      <div className="aw-grid__footer aw-grid__footer--inside">
        <span className="aw-grid__range" aria-live="polite">
          {texts.range(from, to, paging.of ?? total)}
          {paging.note !== undefined && ` · ${paging.note}`}
        </span>
        <nav className="aw-grid__pages" aria-label={texts.pages}>
          <button type="button" className="aw-grid__page aw-grid__page--small" aria-label={texts.previous} disabled={page <= 1} onClick={() => onPage(page - 1)}>
            ‹
          </button>
          <button type="button" className="aw-grid__page aw-grid__page--small" aria-label={texts.next} disabled={page >= count} onClick={() => onPage(page + 1)}>
            ›
          </button>
        </nav>
      </div>
    );
  }

  return (
    <div className="aw-grid__footer">
      <span className="aw-grid__range" aria-live="polite">
        {texts.range(from, to, total)}
      </span>
      {total > pageSizes[0] && (
        <span className="aw-grid__size">
          <span id={`${id}-size`} className="aw-visually-hidden">
            {texts.rowsPerPage}
          </span>
          <ComboBox
            labelledBy={`${id}-size`}
            options={pageSizes.map((size) => ({ value: String(size), label: texts.perPage(size) }))}
            value={String(pageSize)}
            onChange={(value) => {
              onPageSize(Number(value));
              onPage(1);
            }}
            texts={{ choose: texts.rowsPerPage }}
          />
        </span>
      )}
      {count > 1 && (
        <nav className="aw-grid__pages" aria-label={texts.pages}>
          <button type="button" className="aw-grid__page" aria-label={texts.previous} disabled={page <= 1} onClick={() => onPage(page - 1)}>
            ‹
          </button>
          {pageItems(page, count).map((item, index) =>
            item === "gap" ? (
              <span key={`gap-${index}`} className="aw-grid__gap" aria-hidden="true">
                …
              </span>
            ) : (
              <button
                key={item}
                type="button"
                className="aw-grid__page aw-grid__page--number"
                aria-label={texts.page(item)}
                aria-current={item === page ? "page" : undefined}
                onClick={() => onPage(item)}
              >
                {item}
              </button>
            ),
          )}
          <button type="button" className="aw-grid__page" aria-label={texts.next} disabled={page >= count} onClick={() => onPage(page + 1)}>
            ›
          </button>
        </nav>
      )}
    </div>
  );
}
