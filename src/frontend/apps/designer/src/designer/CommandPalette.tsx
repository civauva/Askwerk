// SPDX-License-Identifier: AGPL-3.0-only
import type { Translate } from "@askwerk/schema";
import { useId, useState, type KeyboardEvent } from "react";
import type { DesignerMessageKey } from "./messages";

export interface Command {
  readonly id: string;
  readonly label: string;
  readonly run: () => void;
}

type CommandPaletteProps = {
  t: Translate<DesignerMessageKey>;
  commands: readonly Command[];
  onClose: () => void;
};

/** Ctrl K: everything in the designer by typing (ui-design-system.md, Designer). Every word typed must appear in the command. */
export function CommandPalette({ t, commands, onClose }: CommandPaletteProps) {
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const id = useId();
  const words = query.toLowerCase().split(/\s+/).filter((word) => word !== "");
  const shown = commands.filter((command) => words.every((word) => command.label.toLowerCase().includes(word))).slice(0, 12);
  const highlighted = shown[Math.min(active, shown.length - 1)];

  function run(command: Command) {
    onClose();
    command.run();
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      setActive((current) => (Math.min(current, shown.length - 1) + step + shown.length) % Math.max(shown.length, 1));
    } else if (event.key === "Enter" && highlighted !== undefined) {
      event.preventDefault();
      run(highlighted);
    } else if (event.key === "Escape") {
      event.preventDefault();
      onClose();
    }
  }

  return (
    <div className="designer-scrim designer-scrim--top" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div role="dialog" aria-modal="true" aria-label={t("commands")} className="designer-palette">
        <input
          role="combobox"
          aria-label={t("command")}
          aria-expanded="true"
          aria-controls={`${id}-list`}
          aria-activedescendant={highlighted === undefined ? undefined : `${id}-${highlighted.id}`}
          placeholder={t("commandPlaceholder")}
          autoFocus
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setActive(0);
          }}
          onKeyDown={onKeyDown}
        />
        <ul role="listbox" id={`${id}-list`} aria-label={t("commands")}>
          {shown.map((command) => (
            <li
              key={command.id}
              id={`${id}-${command.id}`}
              role="option"
              aria-selected={command === highlighted}
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => run(command)}
            >
              {command.label}
            </li>
          ))}
        </ul>
        {shown.length === 0 && <p className="designer-note">{t("noCommands")}</p>}
      </div>
    </div>
  );
}
