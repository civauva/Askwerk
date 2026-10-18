// SPDX-License-Identifier: AGPL-3.0-only
import { useState, type FocusEvent, type PointerEvent } from "react";
import { createPortal } from "react-dom";
import { lucide, type IconName } from "./icons/lucide";

/** A Lucide icon (agents/ui-design-system.md, Icons), 18 px, drawn in the text colour. */
export function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  return (
    <svg className="aw-icon" width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      {lucide[name].map(([Tag, attributes], index) => (
        <Tag key={index} {...attributes} />
      ))}
    </svg>
  );
}

export type RowTool = {
  icon: IconName;
  /** What it does, shown as the tooltip, e.g. "Suspend". */
  label: string;
  /** The accessible name when it should say more than the label, e.g. "Suspend: Acme GmbH". */
  name?: string;
  /** A destructive action: red when pointed at; it should ask before it acts. */
  danger?: boolean;
  onClick: () => void;
};

type Tip = { text: string; x: number; y: number };

/**
 * A row's actions as one small toolbar (W42b B, AWK-62): icons in a framed pill with thin dividers, each with a tooltip
 * naming it. The tooltip is drawn above everything, as grid cells clip what overflows them.
 */
export function RowTools({ label, tools }: { label: string; tools: readonly RowTool[] }) {
  const [tip, setTip] = useState<Tip | null>(null);

  if (tools.length === 0) {
    return null;
  }

  const show = (text: string) => (event: PointerEvent<HTMLButtonElement> | FocusEvent<HTMLButtonElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    setTip({ text, x: box.left + box.width / 2, y: box.top });
  };
  const hide = () => setTip(null);

  return (
    <span className="aw-row-tools" role="group" aria-label={label}>
      {tools.map((tool) => (
        <button
          key={tool.label}
          type="button"
          className={tool.danger === true ? "aw-row-tools__tool aw-row-tools__tool--danger" : "aw-row-tools__tool"}
          aria-label={tool.name ?? tool.label}
          onPointerEnter={show(tool.label)}
          onPointerLeave={hide}
          onFocus={show(tool.label)}
          onBlur={hide}
          onClick={() => {
            hide();
            tool.onClick();
          }}
        >
          <Icon name={tool.icon} />
        </button>
      ))}
      {tip !== null &&
        createPortal(
          <span className="aw-tip" role="tooltip" style={{ left: tip.x, top: tip.y }}>
            {tip.text}
          </span>,
          document.body,
        )}
    </span>
  );
}
