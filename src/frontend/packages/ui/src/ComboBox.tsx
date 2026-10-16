// SPDX-License-Identifier: AGPL-3.0-only
import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useDismiss } from "./dismiss";

export type ComboBoxOption = { readonly value: string; readonly label: string };

export type ComboBoxTexts = {
  /** Shown while nothing is chosen. */
  readonly choose: string;
  /** Accessible name of the search field; needed from searchFrom options on, like results and noResults. */
  readonly search?: string;
  readonly results?: (shown: number, total: number) => string;
  readonly noResults?: string;
};

type ComboBoxProps = {
  /** Id of the visible label; the control is named by it. */
  labelledBy: string;
  options: readonly ComboBoxOption[];
  value: string | null;
  onChange: (value: string) => void;
  texts: ComboBoxTexts;
  /** From this many options on, the list gets a search field (W7: 10). */
  searchFrom?: number;
  id?: string;
  invalid?: boolean;
  describedBy?: string;
};

/** Case and accents do not matter when searching: "polten" finds "St. Pölten". */
function fold(text: string): string {
  return text.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase();
}

/**
 * The one select of Askwerk (AWK-19): a button that opens a list, with a search field for long lists. Follows the
 * WAI-ARIA combobox and listbox patterns: arrows move, Enter picks, Escape closes, the focus returns to the button.
 */
export function ComboBox({ labelledBy, options, value, onChange, texts, searchFrom = 10, id, invalid, describedBy }: ComboBoxProps) {
  const ownId = useId();
  const base = id ?? ownId;
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const button = useRef<HTMLButtonElement>(null);
  const root = useRef<HTMLSpanElement>(null);
  const search = useRef<HTMLInputElement>(null);
  const list = useRef<HTMLUListElement>(null);
  const searchable = options.length >= searchFrom;
  const chosen = options.find((option) => option.value === value);
  const shown = useMemo(() => (query.trim() === "" ? options : options.filter((option) => fold(option.label).includes(fold(query.trim())))), [options, query]);
  const optionId = (index: number) => `${base}-option-${index}`;

  useEffect(() => {
    if (open) {
      (searchable ? search.current : list.current)?.focus();
    }
  }, [open, searchable]);

  useEffect(() => {
    list.current?.querySelector(`#${CSS.escape(optionId(active))}`)?.scrollIntoView?.({ block: "nearest" });
  });

  function show() {
    setQuery("");
    setActive(Math.max(0, options.findIndex((option) => option.value === value)));
    setOpen(true);
  }

  function close(refocus = true) {
    setOpen(false);

    if (refocus) {
      button.current?.focus();
    }
  }

  useDismiss(open, root, close);

  function pick(option: ComboBoxOption | undefined) {
    if (option !== undefined) {
      onChange(option.value);
    }

    close();
  }

  function onListKeyDown(event: KeyboardEvent) {
    const keys: Record<string, () => void> = {
      ArrowDown: () => setActive((index) => Math.min(shown.length - 1, index + 1)),
      ArrowUp: () => setActive((index) => Math.max(0, index - 1)),
      Home: () => setActive(0),
      End: () => setActive(shown.length - 1),
      Enter: () => pick(shown[active]),
      Escape: () => close(),
      Tab: () => setOpen(false),
    };
    const handle = keys[event.key];

    if (handle !== undefined) {
      if (event.key !== "Tab") {
        event.preventDefault();
      }

      handle();
    }
  }

  const activeId = shown[active] !== undefined ? optionId(active) : undefined;

  return (
    <span className="aw-combo" ref={root}>
      <button
        ref={button}
        id={base}
        type="button"
        className="aw-field aw-combo__button"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-labelledby={`${labelledBy} ${base}`}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        onClick={() => (open ? close() : show())}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            show();
          }
        }}
      >
        <span className={chosen === undefined ? "aw-combo__placeholder" : undefined}>{chosen?.label ?? texts.choose}</span>
        <span aria-hidden="true" className="aw-combo__chevron">
          ▾
        </span>
      </button>
      {open && (
        <span className="aw-combo__popup">
          {searchable && (
            <input
              ref={search}
              className="aw-field aw-combo__search"
              role="combobox"
              aria-label={texts.search}
              aria-expanded="true"
              aria-controls={`${base}-list`}
              aria-autocomplete="list"
              aria-activedescendant={activeId}
              value={query}
              onChange={(event) => {
                setQuery(event.target.value);
                setActive(0);
              }}
              onKeyDown={onListKeyDown}
            />
          )}
          <ul
            ref={list}
            id={`${base}-list`}
            role="listbox"
            aria-labelledby={labelledBy}
            tabIndex={searchable ? -1 : 0}
            aria-activedescendant={searchable ? undefined : activeId}
            className="aw-combo__list"
            onKeyDown={searchable ? undefined : onListKeyDown}
          >
            {shown.map((option, index) => (
              <li
                key={option.value}
                id={optionId(index)}
                role="option"
                aria-selected={option.value === value}
                className={index === active ? "aw-combo__option aw-combo__option--active" : "aw-combo__option"}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => pick(option)}
              >
                {option.label}
              </li>
            ))}
          </ul>
          {searchable && (
            <span className="aw-combo__count" aria-live="polite">
              {shown.length === 0 ? texts.noResults : texts.results?.(shown.length, options.length)}
            </span>
          )}
        </span>
      )}
    </span>
  );
}
