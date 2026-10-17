// SPDX-License-Identifier: AGPL-3.0-only
// The standard controls (AWK-19, W20): one look and one behaviour for dialogs, menus, tabs, switches, checkboxes,
// radios, fields and notices, used by every screen.
import {
  Children,
  cloneElement,
  isValidElement,
  useEffect,
  useId,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactElement,
  type ReactNode,
  type Ref,
} from "react";
import { useDismiss } from "./dismiss";

const focusable = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

type DialogProps = {
  title: ReactNode;
  onClose: () => void;
  children: ReactNode;
  /** 720 px instead of 560 px. */
  wide?: boolean;
  /** A line above the title, e.g. "Publish version 4". */
  kicker?: ReactNode;
  className?: string;
  /** More in the head beside or below the title, e.g. steps (W35). */
  head?: ReactNode;
  /** A bar below the content that stays in view, e.g. the actions (W35). */
  footer?: ReactNode;
};

/**
 * A modal dialog (W20 1): title, content, Escape and ✕ close it, focus stays inside and goes back where it was.
 * On phones it opens as a sheet from the bottom.
 */
export function Dialog({ title, onClose, children, wide = false, kicker, className, head, footer }: DialogProps) {
  const id = useId();
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const before = document.activeElement as HTMLElement | null;
    const first = box.current?.querySelector<HTMLElement>("[autofocus]") ?? box.current?.querySelector<HTMLElement>(focusable);
    (first ?? box.current)?.focus();

    return () => before?.focus?.();
  }, []);

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === "Escape") {
      event.stopPropagation();
      onClose();
      return;
    }

    if (event.key !== "Tab" || box.current === null) {
      return;
    }

    const items = [...box.current.querySelectorAll<HTMLElement>(focusable)];

    if (items.length === 0) {
      return;
    }

    const [first, last] = [items[0]!, items[items.length - 1]!];

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  return (
    <div className="aw-backdrop" onClick={(event) => event.stopPropagation()}>
      <div
        ref={box}
        className={`aw-dialog${wide ? " aw-dialog--wide" : ""}${className ? ` ${className}` : ""}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-title`}
        tabIndex={-1}
        onKeyDown={onKeyDown}
      >
        <div className="aw-dialog__head">
          {kicker !== undefined && <span className="aw-dialog__kicker">{kicker}</span>}
          <h2 id={`${id}-title`}>{title}</h2>
          {head}
        </div>
        <div className="aw-dialog__body">{children}</div>
        {footer !== undefined && footer !== null && footer !== false && <div className="aw-dialog__foot">{footer}</div>}
      </div>
    </div>
  );
}

/** The buttons row of a dialog: secondary first, the action last. */
export function DialogActions({ children }: { children: ReactNode }) {
  return <div className="aw-dialog__actions">{children}</div>;
}

type ConfirmProps = {
  title: string;
  text: ReactNode;
  confirm: string;
  cancel: string;
  onConfirm: () => void;
  onCancel: () => void;
};

/** A question before a risky step (W20 1): Cancel has the focus, the action is the primary button. */
export function ConfirmDialog({ title, text, confirm, cancel, onConfirm, onCancel }: ConfirmProps) {
  return (
    <Dialog title={title} onClose={onCancel}>
      <p>{text}</p>
      <DialogActions>
        <button type="button" className="aw-button aw-button--secondary" autoFocus onClick={onCancel}>
          {cancel}
        </button>
        <button type="button" className="aw-button aw-button--primary" onClick={onConfirm}>
          {confirm}
        </button>
      </DialogActions>
    </Dialog>
  );
}

type TriggerProps = {
  ref: Ref<HTMLButtonElement>;
  "aria-haspopup": "menu";
  "aria-expanded": boolean;
  onClick: () => void;
};

type MenuProps = {
  /** The menu's accessible name. */
  label: string;
  /** The button that opens it; spread the props onto a button. */
  trigger: (props: TriggerProps) => ReactNode;
  children: ReactNode;
  /** Opens to the left of the button. */
  alignEnd?: boolean;
  className?: string;
};

/**
 * A menu button and its menu (W20 2): arrow keys, Home and End move, Escape closes and returns the focus, a click
 * outside closes it. Items are MenuItem, MenuLink and MenuSeparator, or any element with a menuitem role.
 */
export function Menu({ label, trigger, children, alignEnd = true, className }: MenuProps) {
  const [open, setOpen] = useState(false);
  const anchor = useRef<HTMLSpanElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const list = useRef<HTMLDivElement>(null);

  useDismiss(open, anchor, (refocus) => {
    setOpen(false);

    if (refocus) {
      button.current?.focus();
    }
  });

  useEffect(() => {
    if (open) {
      list.current?.querySelector<HTMLElement>("[role^='menuitem']")?.focus();
    }
  }, [open]);

  const onKeyDown = (event: KeyboardEvent) => {
    const items = [...(list.current?.querySelectorAll<HTMLElement>("[role^='menuitem']") ?? [])];
    const at = items.indexOf(document.activeElement as HTMLElement);
    const go = (index: number) => {
      event.preventDefault();
      items[(index + items.length) % items.length]?.focus();
    };

    if (event.key === "ArrowDown") {
      go(at + 1);
    } else if (event.key === "ArrowUp") {
      go(at - 1);
    } else if (event.key === "Home") {
      go(0);
    } else if (event.key === "End") {
      go(items.length - 1);
    }
  };

  const close = () => setOpen(false);
  const items = Children.map(children, (child) =>
    isValidElement<{ onSelect?: () => void }>(child) && child.props.onSelect !== undefined
      ? cloneElement(child as ReactElement<{ onSelect?: () => void }>, {
          onSelect: () => {
            close();
            child.props.onSelect!();
          },
        })
      : child,
  );

  return (
    <span ref={anchor} className="aw-menu-anchor">
      {trigger({ ref: button, "aria-haspopup": "menu", "aria-expanded": open, onClick: () => setOpen((current) => !current) })}
      {open && (
        <div ref={list} role="menu" aria-label={label} className={`aw-menu${alignEnd ? "" : " aw-menu--start"}${className ? ` ${className}` : ""}`} onKeyDown={onKeyDown} onClick={(event) => (event.target as HTMLElement).closest("a[role='menuitem']") && close()}>
          {items}
        </div>
      )}
    </span>
  );
}

/** An item of a Menu that does something. */
export function MenuItem({ onSelect, children, hint, disabled = false }: { onSelect: () => void; children: ReactNode; hint?: ReactNode; disabled?: boolean }) {
  return (
    <button type="button" role="menuitem" tabIndex={-1} className="aw-menu__item" onClick={onSelect} disabled={disabled} aria-disabled={disabled || undefined}>
      <span>{children}</span>
      {hint !== undefined && <span className="aw-menu__hint" aria-hidden="true">{hint}</span>}
    </button>
  );
}

/** An item of a Menu that is a link, e.g. a download. */
export function MenuLink({ href, children, newTab = false, hint }: { href: string; children: ReactNode; newTab?: boolean; hint?: ReactNode }) {
  return (
    <a role="menuitem" tabIndex={-1} className="aw-menu__item" href={href} {...(newTab ? { target: "_blank", rel: "noopener" } : {})}>
      <span>{children}</span>
      {hint !== undefined && <span className="aw-menu__hint" aria-hidden="true">{hint}</span>}
    </a>
  );
}

/** A line before risky items. */
export function MenuSeparator() {
  return <div role="separator" className="aw-menu__separator" />;
}

type TabsProps<T extends string> = {
  label: string;
  tabs: readonly (readonly [T, ReactNode])[];
  value: T;
  onChange: (value: T) => void;
  /** Ids for aria-controls: the panel of a tab is idPrefix-panel-value. */
  idPrefix?: string;
};

/** Panel tabs (W20 3): a tablist, arrow keys move between tabs, the selected one is underlined. */
export function Tabs<T extends string>({ label, tabs, value, onChange, idPrefix }: TabsProps<T>) {
  const onKeyDown = (event: KeyboardEvent, index: number) => {
    if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
      event.preventDefault();
      const next = tabs[(index + (event.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length]![0];
      onChange(next);
      (event.currentTarget.parentElement?.querySelectorAll<HTMLElement>("[role='tab']")[tabs.findIndex(([key]) => key === next)])?.focus();
    }
  };

  return (
    <div className="aw-tabs" role="tablist" aria-label={label}>
      {tabs.map(([key, name], index) => (
        <button
          key={key}
          type="button"
          role="tab"
          aria-selected={value === key}
          tabIndex={value === key ? 0 : -1}
          {...(idPrefix === undefined ? {} : { id: `${idPrefix}-tab-${key}`, "aria-controls": `${idPrefix}-panel-${key}` })}
          onClick={() => onChange(key)}
          onKeyDown={(event) => onKeyDown(event, index)}
        >
          {name}
        </button>
      ))}
    </div>
  );
}

/** A switch for a setting that applies at once (W20 4). */
export function Switch({ label, checked, onChange, disabled = false }: { label: ReactNode; checked: boolean; onChange: (checked: boolean) => void; disabled?: boolean }) {
  return (
    <button type="button" role="switch" aria-checked={checked} disabled={disabled} className="aw-switch" onClick={() => onChange(!checked)}>
      <b aria-hidden="true" />
      {label}
    </button>
  );
}

/** A checkbox with its label (W20 4); a native input underneath. */
export function Checkbox({ label, checked, onChange, disabled = false, hint }: { label: ReactNode; checked: boolean; onChange: (checked: boolean) => void; disabled?: boolean; hint?: ReactNode }) {
  return (
    <label className="aw-check">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />
      <span>
        {label}
        {hint !== undefined && <span className="aw-check__hint">{hint}</span>}
      </span>
    </label>
  );
}

type RadioGroupProps<T extends string> = {
  legend: ReactNode;
  value: T;
  options: readonly { value: T; label: ReactNode; hint?: ReactNode; disabled?: boolean }[];
  onChange: (value: T) => void;
  /** Visually hidden legend. */
  hideLegend?: boolean;
};

/** Radios in a group with a legend (W20 4). */
export function RadioGroup<T extends string>({ legend, value, options, onChange, hideLegend = false }: RadioGroupProps<T>) {
  const name = useId();

  return (
    <fieldset className="aw-radios">
      <legend className={hideLegend ? "aw-visually-hidden" : "aw-label"}>{legend}</legend>
      {options.map((option) => (
        <label key={option.value} className="aw-check aw-check--radio">
          <input type="radio" name={name} value={option.value} checked={value === option.value} disabled={option.disabled} onChange={() => onChange(option.value)} />
          <span>
            {option.label}
            {option.hint !== undefined && <span className="aw-check__hint">{option.hint}</span>}
          </span>
        </label>
      ))}
    </fieldset>
  );
}

type FieldProps = {
  label: ReactNode;
  /** The input; it gets the id, aria-describedby and aria-invalid. */
  children: ReactElement<{ id?: string; "aria-describedby"?: string; "aria-invalid"?: boolean }>;
  hint?: ReactNode;
  /** Replaces the hint when given. */
  error?: ReactNode;
};

/** A label, its input, and a hint or an error, linked for screen readers (W20 5). */
export function Field({ label, children, hint, error }: FieldProps) {
  const id = useId();
  const note = error ?? hint;

  return (
    <div className="aw-formfield">
      <label className="aw-label" htmlFor={`${id}-input`}>
        {label}
      </label>
      {cloneElement(children, { id: `${id}-input`, ...(note === undefined ? {} : { "aria-describedby": `${id}-note` }), ...(error === undefined ? {} : { "aria-invalid": true }) })}
      {note !== undefined && (
        <span id={`${id}-note`} className={error === undefined ? "aw-formfield__hint" : "aw-formfield__error"} role={error === undefined ? undefined : "alert"}>
          {note}
        </span>
      )}
    </div>
  );
}

/**
 * A done message (W20 6): role status, it closes by itself after 5 seconds or with ✕; or an error with its trace
 * reference (role alert) that stays until closed.
 */
export function Notice({ children, kind = "done", onClose, closeLabel, reference }: { children: ReactNode; kind?: "done" | "error"; onClose: () => void; closeLabel: string; reference?: string }) {
  useEffect(() => {
    if (kind !== "done") {
      return undefined;
    }

    const timer = setTimeout(onClose, 5000);

    return () => clearTimeout(timer);
  }, [kind, onClose]);

  return (
    <div className={`aw-notice aw-notice--${kind}`} role={kind === "done" ? "status" : "alert"}>
      <span>
        {children}
        {reference !== undefined && <code className="aw-notice__reference">{reference}</code>}
      </span>
      <button type="button" className="aw-notice__close" aria-label={closeLabel} onClick={onClose}>
        ✕
      </button>
    </div>
  );
}
