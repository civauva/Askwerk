// SPDX-License-Identifier: AGPL-3.0-only
import type { Question, QuestionType, Translate } from "@askwerk/schema";
import { useDismiss } from "@askwerk/ui";
import { useId, useRef, useState, type KeyboardEvent } from "react";
import type { DesignerMessageKey } from "./messages";

/** One entry of the menu: a type, or a type with settings (Email is a short text with a format; W7). */
type Entry = {
  readonly key: string;
  readonly type: QuestionType;
  readonly name?: DesignerMessageKey;
  readonly extra?: Partial<Question>;
  /** Number key that inserts it directly; the original ten types have one (M1). */
  readonly number?: string;
};

const groups: readonly (readonly [DesignerMessageKey, readonly Entry[]])[] = [
  [
    "groupChoice",
    [
      { key: "single", type: "single", number: "1" },
      { key: "multiple", type: "multiple", number: "2" },
      { key: "dropdown", type: "dropdown" },
      { key: "yes-no", type: "yes-no" },
      { key: "ranking", type: "ranking", number: "0" },
      { key: "matrix", type: "matrix", number: "9" },
    ],
  ],
  [
    "groupScales",
    [
      { key: "rating", type: "rating", number: "3" },
      { key: "nps", type: "nps", number: "4" },
      { key: "slider", type: "slider" },
    ],
  ],
  [
    "groupText",
    [
      { key: "short-text", type: "short-text", number: "5" },
      { key: "long-text", type: "long-text", number: "6" },
      { key: "number", type: "number", number: "7" },
      { key: "email", type: "short-text", name: "kindEmail", extra: { format: "email" } },
      { key: "phone", type: "short-text", name: "kindPhone", extra: { format: "phone" } },
      { key: "url", type: "short-text", name: "kindUrl", extra: { format: "url" } },
    ],
  ],
  [
    "groupDate",
    [
      { key: "date", type: "date", number: "8" },
      { key: "date-time", type: "date", name: "kindDateTime", extra: { mode: "date-time", minuteStep: 15 } },
      { key: "time", type: "date", name: "kindTime", extra: { mode: "time", minuteStep: 15 } },
    ],
  ],
  ["groupOther", [{ key: "text-block", type: "text-block" }, { key: "image", type: "image" }]],
];

const entries = groups.flatMap(([, items]) => items);

type TypeMenuProps = {
  t: Translate<DesignerMessageKey>;
  typeNames: Record<QuestionType, string>;
  onPick: (type: QuestionType, extra?: Partial<Question>) => void;
  onClose: () => void;
};

/** Filter by typing, ↑↓ to choose, Enter to insert, a number key to insert directly, Esc to close. Grouped as in W7. */
export function TypeMenu({ t, typeNames, onPick, onClose }: TypeMenuProps) {
  const [filter, setFilter] = useState("");
  const [active, setActive] = useState(0);
  const id = useId();
  const root = useRef<HTMLDivElement>(null);
  const nameOf = (entry: Entry) => (entry.name !== undefined ? t(entry.name) : typeNames[entry.type]);
  const matches = (entry: Entry) => nameOf(entry).toLowerCase().includes(filter.trim().toLowerCase());
  const shown = entries.filter(matches);
  const highlighted = shown[Math.min(active, shown.length - 1)];
  const pick = (entry: Entry) => onPick(entry.type, entry.extra);

  // A click outside or Tab away closes the menu as well (AWK-19).
  useDismiss(true, root, () => onClose());

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    const byNumber = filter === "" ? entries.find((entry) => entry.number !== undefined && entry.number === event.key) : undefined;

    if (byNumber !== undefined) {
      event.preventDefault();
      pick(byNumber);
    } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      setActive((current) => (Math.min(current, shown.length - 1) + step + shown.length) % shown.length);
    } else if (event.key === "Enter" && highlighted !== undefined) {
      event.preventDefault();
      pick(highlighted);
    } else if (event.key === "Escape") {
      event.preventDefault();
      onClose();
    }
  }

  return (
    <div className="designer-typemenu" ref={root}>
      <input
        className="designer-typemenu__filter"
        role="combobox"
        aria-label={t("questionType")}
        aria-expanded="true"
        aria-controls={`${id}-list`}
        aria-activedescendant={highlighted === undefined ? undefined : `${id}-${highlighted.key}`}
        autoFocus
        value={filter}
        onChange={(event) => {
          setFilter(event.target.value);
          setActive(0);
        }}
        onKeyDown={onKeyDown}
      />
      <div className="designer-typemenu__list" role="listbox" id={`${id}-list`} aria-label={t("questionType")}>
        {groups.map(([group, items]) => {
          const visible = items.filter(matches);

          return (
            visible.length > 0 && (
              <ul key={group} role="group" aria-label={t(group)} className="designer-typemenu__group">
                <li role="presentation" className="designer-typemenu__heading" aria-hidden="true">
                  {t(group)}
                </li>
                {visible.map((entry) => (
                  <li
                    key={entry.key}
                    id={`${id}-${entry.key}`}
                    role="option"
                    aria-selected={entry.key === highlighted?.key}
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => pick(entry)}
                  >
                    {nameOf(entry)}
                    {entry.number !== undefined && (
                      <>
                        {" "}
                        <kbd>{entry.number}</kbd>
                      </>
                    )}
                  </li>
                ))}
              </ul>
            )
          );
        })}
      </div>
      <p className="designer-typemenu__hint" aria-hidden="true">
        {t("typeMenuHint")}
      </p>
    </div>
  );
}
