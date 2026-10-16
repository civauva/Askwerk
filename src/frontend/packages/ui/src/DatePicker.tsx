// SPDX-License-Identifier: AGPL-3.0-only
import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { useDismiss } from "./dismiss";
// Icons: Lucide "calendar" and "clock" (ISC licence), as agents/ui-design-system.md asks.
import { addDays, addMonths, formatDate, fromDate, parseDate, parseTime, toDate } from "./dates";

export type DatePickerTexts = {
  readonly openCalendar: string;
  readonly previousMonth: string;
  readonly nextMonth: string;
  /** Shown at the field when the typed text is no date. */
  readonly invalid: string;
};

type DatePickerProps = {
  /** Id of the text field; a <label htmlFor> names it. */
  id: string;
  value: string | null;
  onChange: (value: string | null) => void;
  locale: string;
  texts: DatePickerTexts;
  min?: string | undefined;
  max?: string | undefined;
  describedBy?: string;
};

function region(locale: string) {
  return locale === "en" ? "en-GB" : locale;
}

/** Monday of the week the date is in. */
function weekStart(value: string): string {
  const weekday = (toDate(value).getUTCDay() + 6) % 7;

  return addDays(value, -weekday);
}

/**
 * The one date field of Askwerk (AWK-19): type the date the way your language writes it, or pick it from a calendar.
 * The calendar follows the WAI-ARIA date picker dialog pattern; weeks start on Monday.
 */
export function DatePicker({ id, value, onChange, locale, texts, min, max, describedBy }: DatePickerProps) {
  const [text, setText] = useState(value === null ? "" : formatDate(value, locale));
  const [invalid, setInvalid] = useState(false);
  const [open, setOpen] = useState(false);
  const [focused, setFocused] = useState(value ?? fromDate(new Date()));
  const opener = useRef<HTMLButtonElement>(null);
  const grid = useRef<HTMLTableElement>(null);
  const root = useRef<HTMLSpanElement>(null);
  const titleId = useId();
  const outOfRange = (day: string) => (min !== undefined && day < min) || (max !== undefined && day > max);

  useEffect(() => {
    setText(value === null ? "" : formatDate(value, locale));
  }, [value, locale]);

  useEffect(() => {
    if (open) {
      grid.current?.querySelector<HTMLButtonElement>(`[data-day="${focused}"]`)?.focus();
    }
  }, [open, focused]);

  function commit() {
    if (text.trim() === "") {
      setInvalid(false);
      onChange(null);
      return;
    }

    const parsed = parseDate(text, locale);
    setInvalid(parsed === null || outOfRange(parsed));

    if (parsed !== null && !outOfRange(parsed)) {
      onChange(parsed);
      setText(formatDate(parsed, locale));
    }
  }

  function close(refocus = true) {
    setOpen(false);

    if (refocus) {
      opener.current?.focus();
    }
  }

  useDismiss(open, root, close);

  function pick(day: string) {
    if (!outOfRange(day)) {
      setInvalid(false);
      onChange(day);
      close();
    }
  }

  function onDayKeyDown(event: KeyboardEvent) {
    const moves: Record<string, () => string> = {
      ArrowLeft: () => addDays(focused, -1),
      ArrowRight: () => addDays(focused, 1),
      ArrowUp: () => addDays(focused, -7),
      ArrowDown: () => addDays(focused, 7),
      Home: () => weekStart(focused),
      End: () => addDays(weekStart(focused), 6),
      PageUp: () => addMonths(focused, event.shiftKey ? -12 : -1),
      PageDown: () => addMonths(focused, event.shiftKey ? 12 : 1),
    };

    if (moves[event.key] !== undefined) {
      event.preventDefault();
      setFocused(moves[event.key]!());
    } else if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      pick(focused);
    } else if (event.key === "Escape") {
      event.preventDefault();
      close();
    }
  }

  const monthStart = `${focused.slice(0, 8)}01`;
  const firstShown = weekStart(monthStart);
  const weeks = Array.from({ length: 6 }, (_, week) => Array.from({ length: 7 }, (_, day) => addDays(firstShown, week * 7 + day))).filter(
    (week, index) => index === 0 || week[0]!.slice(0, 7) === monthStart.slice(0, 7),
  );
  const monthLabel = new Intl.DateTimeFormat(region(locale), { month: "long", year: "numeric", timeZone: "UTC" }).format(toDate(monthStart));
  const dayLabel = new Intl.DateTimeFormat(region(locale), { day: "numeric", month: "long", year: "numeric", timeZone: "UTC" });
  const weekdays = Array.from({ length: 7 }, (_, index) => toDate(addDays(firstShown, index)));
  const errorId = `${id}-error`;

  return (
    <span className="aw-date" ref={root}>
      <span className="aw-date__row">
        <input
          id={id}
          className="aw-field aw-date__input"
          inputMode="numeric"
          autoComplete="off"
          value={text}
          aria-invalid={invalid || undefined}
          aria-describedby={[describedBy, invalid ? errorId : undefined].filter(Boolean).join(" ") || undefined}
          onChange={(event) => setText(event.target.value)}
          onBlur={commit}
          onKeyDown={(event) => event.key === "Enter" && commit()}
        />
        <button
          ref={opener}
          type="button"
          className="aw-field aw-date__open"
          aria-label={texts.openCalendar}
          aria-haspopup="dialog"
          aria-expanded={open}
          onClick={() => {
            setFocused(value ?? parseDate(text, locale) ?? fromDate(new Date()));
            setOpen(!open);
          }}
        >
          <svg className="aw-icon" viewBox="0 0 24 24" aria-hidden="true">
            <rect x="3" y="4" width="18" height="18" rx="2" />
            <path d="M16 2v4M8 2v4M3 10h18" />
          </svg>
        </button>
      </span>
      {invalid && (
        <span id={errorId} className="aw-field-error">
          {texts.invalid}
        </span>
      )}
      {open && (
        <span className="aw-date__popup" role="dialog" aria-modal="false" aria-labelledby={titleId}>
          <span className="aw-date__head">
            <button type="button" className="aw-date__nav" aria-label={texts.previousMonth} onClick={() => setFocused(addMonths(focused, -1))}>
              <span aria-hidden="true">‹</span>
            </button>
            <span id={titleId} className="aw-date__title" aria-live="polite">
              {monthLabel}
            </span>
            <button type="button" className="aw-date__nav" aria-label={texts.nextMonth} onClick={() => setFocused(addMonths(focused, 1))}>
              <span aria-hidden="true">›</span>
            </button>
          </span>
          <table ref={grid} role="grid" aria-labelledby={titleId} className="aw-date__grid">
            <thead>
              <tr>
                {weekdays.map((day) => (
                  <th key={day.toISOString()} scope="col" abbr={new Intl.DateTimeFormat(region(locale), { weekday: "long", timeZone: "UTC" }).format(day)}>
                    {new Intl.DateTimeFormat(region(locale), { weekday: "short", timeZone: "UTC" }).format(day).slice(0, 2)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {weeks.map((week) => (
                <tr key={week[0]}>
                  {week.map((day) => {
                    const inMonth = day.slice(0, 7) === monthStart.slice(0, 7);

                    return (
                      <td key={day} role="gridcell" aria-selected={day === value}>
                        <button
                          type="button"
                          data-day={day}
                          tabIndex={day === focused ? 0 : -1}
                          disabled={outOfRange(day)}
                          aria-label={dayLabel.format(toDate(day))}
                          className={["aw-date__day", inMonth ? "" : "aw-date__day--other", day === value ? "aw-date__day--chosen" : ""].join(" ").trim()}
                          onClick={() => pick(day)}
                          onKeyDown={onDayKeyDown}
                        >
                          {Number(day.slice(8))}
                        </button>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </span>
      )}
    </span>
  );
}

type TimeFieldProps = {
  id: string;
  value: string | null;
  onChange: (value: string | null) => void;
  texts: { readonly chooseTime: string; readonly invalid: string };
  minuteStep?: number;
  describedBy?: string;
};

/** The one time field (AWK-19): type a time ("930", "9:30", "21") on the 24-hour clock, or pick one in minute steps. */
export function TimeField({ id, value, onChange, texts, minuteStep = 15, describedBy }: TimeFieldProps) {
  const [text, setText] = useState(value ?? "");
  const [invalid, setInvalid] = useState(false);
  const [open, setOpen] = useState(false);
  const times = Array.from({ length: Math.ceil(1440 / minuteStep) }, (_, index) => {
    const minutes = index * minuteStep;
    return `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
  });
  const [active, setActive] = useState(0);
  const opener = useRef<HTMLButtonElement>(null);
  const list = useRef<HTMLUListElement>(null);
  const root = useRef<HTMLSpanElement>(null);
  const errorId = `${id}-error`;

  useEffect(() => setText(value ?? ""), [value]);

  useEffect(() => {
    if (open) {
      list.current?.focus();
      list.current?.querySelector(`[data-index="${active}"]`)?.scrollIntoView?.({ block: "center" });
    }
  }, [open, active]);

  function commit() {
    if (text.trim() === "") {
      setInvalid(false);
      onChange(null);
      return;
    }

    const parsed = parseTime(text);
    setInvalid(parsed === null);

    if (parsed !== null) {
      onChange(parsed);
      setText(parsed);
    }
  }

  function pick(time: string | undefined, refocus = true) {
    if (time !== undefined) {
      setInvalid(false);
      onChange(time);
    }

    setOpen(false);

    if (refocus) {
      opener.current?.focus();
    }
  }

  useDismiss(open, root, (refocus) => pick(undefined, refocus));

  return (
    <span className="aw-date" ref={root}>
      <span className="aw-date__row">
        <input
          id={id}
          className="aw-field aw-time__input"
          inputMode="numeric"
          autoComplete="off"
          value={text}
          aria-invalid={invalid || undefined}
          aria-describedby={[describedBy, invalid ? errorId : undefined].filter(Boolean).join(" ") || undefined}
          onChange={(event) => setText(event.target.value)}
          onBlur={commit}
          onKeyDown={(event) => event.key === "Enter" && commit()}
        />
        <button
          ref={opener}
          type="button"
          className="aw-field aw-date__open"
          aria-label={texts.chooseTime}
          aria-haspopup="listbox"
          aria-expanded={open}
          onClick={() => {
            const current = value ?? parseTime(text);
            setActive(Math.max(0, current === null ? times.indexOf("09:00") : times.findIndex((time) => time >= current)));
            setOpen(!open);
          }}
        >
          <svg className="aw-icon" viewBox="0 0 24 24" aria-hidden="true">
            <circle cx="12" cy="12" r="10" />
            <path d="M12 6v6l4 2" />
          </svg>
        </button>
      </span>
      {invalid && (
        <span id={errorId} className="aw-field-error">
          {texts.invalid}
        </span>
      )}
      {open && (
        <ul
          ref={list}
          role="listbox"
          aria-label={texts.chooseTime}
          tabIndex={0}
          aria-activedescendant={`${id}-time-${active}`}
          className="aw-combo__list aw-time__list"
          onKeyDown={(event) => {
            const keys: Record<string, () => void> = {
              ArrowDown: () => setActive(Math.min(times.length - 1, active + 1)),
              ArrowUp: () => setActive(Math.max(0, active - 1)),
              Enter: () => pick(times[active]),
              Escape: () => pick(undefined),
            };

            if (keys[event.key] !== undefined) {
              event.preventDefault();
              keys[event.key]!();
            }
          }}
        >
          {times.map((time, index) => (
            <li
              key={time}
              id={`${id}-time-${index}`}
              data-index={index}
              role="option"
              aria-selected={time === value}
              className={index === active ? "aw-combo__option aw-combo__option--active" : "aw-combo__option"}
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => pick(time)}
            >
              {time}
            </li>
          ))}
        </ul>
      )}
    </span>
  );
}
