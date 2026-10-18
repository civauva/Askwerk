// SPDX-License-Identifier: AGPL-3.0-only
import { createTranslator, type Locale } from "@askwerk/schema";
import { createContext, Fragment, useContext, useMemo, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { shellMessages, type ShellMessageKey } from "./messages";

/** Where the page's bar goes: the top of the shell's main area (W39); null outside the shell, e.g. in tests. */
export const BarSlot = createContext<HTMLElement | null>(null);

/** How a crumb opens its page. */
export const BarNavigate = createContext<((path: string) => void) | null>(null);

/** The shell's language, for the crumbs that are places of the sidebar. */
export const BarLocale = createContext<Locale>("en");

/** A step before this page: a place of the sidebar (its name), opened by its path, or a group without a page. */
export type Crumb = { place: ShellMessageKey; to?: string } | { label: string; to?: string };

type PageBarProps = {
  /** The steps before this page, e.g. Administration (no page) › Developers & API (/developers). */
  path?: readonly Crumb[];
  /** This page: the last crumb, bold and black. */
  title: string;
  /** Beside the title, e.g. a status chip. */
  extra?: ReactNode;
  /** The page's main actions, at the bar's right end. */
  actions?: ReactNode;
  label?: string;
};

/**
 * The one top bar of every page (W39, AWK-53): where you are on the left, every crumb in the same font and size and
 * only the current page bold and black; the page's actions on the right. It aligns with the page content (32 px).
 */
export function PageBar({ path = [], title, extra, actions, label }: PageBarProps) {
  const slot = useContext(BarSlot);
  const navigate = useContext(BarNavigate);
  const locale = useContext(BarLocale);
  const t = useMemo(() => createTranslator(shellMessages, locale), [locale]);
  const text = (crumb: Crumb) => ("place" in crumb ? t(crumb.place) : crumb.label);
  const bar = (
    <div className="page-bar">
      <nav className="survey-bar__crumbs" aria-label={label ?? t("whereYouAre")}>
        {path.map((crumb) => (
          <Fragment key={text(crumb)}>
            {crumb.to === undefined ? (
              <span className="survey-bar__crumb survey-bar__crumb--text">{text(crumb)}</span>
            ) : (
              <a
                href={crumb.to}
                className="survey-bar__crumb"
                onClick={(event) => {
                  if (navigate !== null) {
                    event.preventDefault();
                    navigate(crumb.to!);
                  }
                }}
              >
                {text(crumb)}
              </a>
            )}
            <span className="survey-bar__crumb-sep" aria-hidden="true">
              ›
            </span>
          </Fragment>
        ))}
        <span className="survey-bar__place" aria-current="page">
          {title}
        </span>
      </nav>
      {extra}
      <span className="page-bar__grow" />
      {actions !== undefined && <div className="page-bar__actions">{actions}</div>}
    </div>
  );

  return slot === null ? bar : createPortal(bar, slot);
}
