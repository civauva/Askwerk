// SPDX-License-Identifier: AGPL-3.0-only
import "./shell.css";
import type { AuthClient, Me } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ConfirmDialog, Dialog, DialogActions, Menu, MenuItem, MenuSeparator } from "@askwerk/ui";
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { roleName } from "../account/ChooseOrganisationPage";
import { accountMessages } from "../account/messages";
import { initialsOf } from "../account/ProfileMenu";
import { Logo } from "../designer/Logo";
import { shellMessages, type ShellMessageKey } from "./messages";
import { BarLocale, BarNavigate, BarSlot } from "./PageBar";

/** A place in the sidebar (AWK-38, W29). */
export type Place = "surveys" | "templates" | "organisation" | "members" | "contacts" | "connections" | "sign-in" | "look" | "messages" | "domains" | "developers" | "organisations" | "administrators" | "installation";

/** Where each place lives. */
export const placePaths: Record<Place, string> = {
  surveys: "/",
  templates: "/templates",
  organisation: "/organisation",
  members: "/members",
  contacts: "/contacts",
  connections: "/connections",
  "sign-in": "/sign-in-settings",
  look: "/look",
  messages: "/messages",
  domains: "/domains",
  developers: "/developers",
  organisations: "/installation/organisations",
  administrators: "/installation/administrators",
  installation: "/installation",
};

/** The place a path belongs to; API keys belong to Developers & API. */
export function placeOf(path: string): Place | null {
  if (path === "/" || path.startsWith("/?") || path.startsWith("/surveys")) {
    return "surveys";
  }

  if (path === "/api-keys" || path === "/developers/drop-in") {
    return "developers";
  }

  if (path === "/connections/new") {
    return "connections";
  }

  const found = (Object.entries(placePaths) as [Place, string][]).find(([, at]) => at !== "/" && path === at);

  return found?.[0] ?? null;
}

/** Inside a survey the sidebar shows the survey's own places; the survey's screens put them here (AWK-38). */
const ShellSlot = createContext<HTMLElement | null>(null);

/** The element the open survey's places go into, or null outside the shell (the embedded designer, tests). */
export const useShellSlot = () => useContext(ShellSlot);

/** Simple or Advanced (W45, AWK-71): what the app shows this person, and how to switch. Outside the shell (tests,
    embedded screens) everything shows. */
export type View = "simple" | "advanced";
export const ShellView = createContext<{ view: View; setView: (view: View) => void }>({ view: "advanced", setView: () => undefined });

/** The person's view, and a way to switch it. */
export function useShellView() {
  return useContext(ShellView);
}

/** The places Simple leaves out of the sidebar; they stay reachable by search and by address. */
const advancedPlaces: ReadonlySet<Place> = new Set<Place>(["contacts", "connections", "sign-in", "messages", "domains", "developers"]);

/** Inside a template (W32) the way back leads to Templates; the screen says when it shows one. */
const ShellTemplate = createContext<(on: boolean) => void>(() => undefined);

/** Tells the shell the open survey is where a template is edited, so its back link says "All templates". */
export function useShellTemplate(on: boolean) {
  const set = useContext(ShellTemplate);

  useEffect(() => {
    set(on);

    return () => set(false);
  }, [set, on]);
}

type Item = { place: Place; label: ShellMessageKey; icon: ReactNode };

/** One of a choice in a Menu (W45): Simple or Advanced, the chosen one ticked. */
function MenuItemRadio({ checked, onSelect, children }: { checked: boolean; onSelect: () => void; children: ReactNode }) {
  return (
    <button type="button" role="menuitemradio" aria-checked={checked} tabIndex={-1} className="aw-menu__item shell-menu__radio" onClick={onSelect}>
      <span>{children}</span>
      <span className="aw-menu__hint" aria-hidden="true">
        {checked ? "✓" : ""}
      </span>
    </button>
  );
}

const icon = (paths: ReactNode) => (
  <svg className="shell-icon" viewBox="0 0 24 24" aria-hidden="true">
    {paths}
  </svg>
);

const icons = {
  surveys: icon(<><rect x="4" y="3" width="16" height="18" rx="1" /><path d="M8 8h8M8 12h8M8 16h5" /></>),
  templates: icon(<><rect x="4" y="3" width="12" height="15" rx="1" /><path d="M8 21h12V7" /></>),
  organisation: icon(<path d="M4 21V4h10v17M14 9h6v12M3 21h18M7 8h3M7 12h3M7 16h3" />),
  members: icon(<><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20a6.5 6.5 0 0 1 13 0M16 4.6a3.5 3.5 0 0 1 0 6.8M21.5 20a6.5 6.5 0 0 0-4.5-6.2" /></>),
  contacts: icon(<><rect x="5" y="3" width="14" height="18" rx="1" /><circle cx="12" cy="10" r="2.6" /><path d="M8 17a4 4 0 0 1 8 0M3 7h2M3 12h2M3 17h2" /></>),
  connections: icon(<><circle cx="6" cy="12" r="3" /><circle cx="18" cy="6" r="3" /><circle cx="18" cy="18" r="3" /><path d="M8.7 10.7l6.6-3.4M8.7 13.3l6.6 3.4" /></>),
  signIn: icon(<><rect x="5" y="11" width="14" height="10" rx="1" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></>),
  look: icon(<><circle cx="12" cy="12" r="8" /><path d="M12 4a8 8 0 0 0 0 16z" fill="currentColor" /></>),
  messages: icon(<><rect x="3" y="5" width="18" height="14" rx="1" /><path d="M3 6l9 7 9-7" /></>),
  domains: icon(<><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18" /></>),
  developers: icon(<path d="M8 7l-5 5 5 5M16 7l5 5-5 5M14 4l-4 16" />),
  organisations: icon(<><rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" /><rect x="3" y="14" width="7" height="7" /><rect x="14" y="14" width="7" height="7" /></>),
  administrators: icon(<path d="M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z" />),
  installation: icon(<path d="M3 12h4l3-8 4 16 3-8h4" />),
  back: icon(<path d="M15 5l-7 7 7 7" />),
};

export type AppShellProps = {
  client: Pick<AuthClient, "signOut" | "switchOrganisation"> & Partial<Pick<AuthClient, "saveView">>;
  locale: Locale;
  me: Me;
  /** The place shown; null for pages outside the places, like Account and security. */
  active: Place | null;
  onNavigate: (path: string) => void;
  onSignedOut: () => void;
  /** After switching organisation; the shell then goes to the new organisation's survey list. */
  onSwitched: () => void;
  onOpenAccount: () => void;
  onLeave?: () => Promise<unknown>;
  /** Inside a survey (W29 drill-in): the way back, and the survey's places come from its screen. */
  survey?: { onBack: () => void; count?: number };
  /** Counts beside places (W35: Contacts 1,284), once known. */
  counts?: Partial<Record<Place, number>>;
  children: ReactNode;
};

/**
 * The app's frame (AWK-38, W29): a sidebar with the organisation, the places and Administration, the person at the
 * foot; inside a survey it steps into the survey. On phones the sidebar is a drawer behind a menu button.
 */
export function AppShell({ client, locale, me, active, onNavigate, onSignedOut, onSwitched, onOpenAccount, onLeave, survey, counts, children }: AppShellProps) {
  const t = createTranslator(shellMessages, locale);
  const a = createTranslator(accountMessages, locale);
  const [slot, setSlot] = useState<HTMLElement | null>(null);
  const [barSlot, setBarSlot] = useState<HTMLElement | null>(null);
  const [inTemplate, setInTemplate] = useState(false);
  const [drawer, setDrawer] = useState(false);
  const [leaving, setLeaving] = useState<"ask" | "refused" | null>(null);
  const [view, setViewState] = useState<View>(me.view === "simple" ? "simple" : "advanced");
  const [note, setNote] = useState<{ view: View; before: View } | null>(null);
  const setView = (next: View, quiet = false) => {
    if (next === view) {
      return;
    }

    setViewState(next);
    void client.saveView?.(next).catch(() => undefined);
    setNote(quiet ? null : { view: next, before: view });
  };

  // The note after a switch says what changed and offers Undo, for a few seconds.
  useEffect(() => {
    if (note === null) {
      return undefined;
    }

    const timer = window.setTimeout(() => setNote(null), 7000);

    return () => window.clearTimeout(timer);
  }, [note]);
  const current = me.organisations.find((organisation) => organisation.id === me.currentOrganisationId);
  const manager = current?.role === "owner" || current?.role === "admin";
  const go = (path: string) => {
    setDrawer(false);
    onNavigate(path);
  };

  const item = ({ place, label, icon: glyph }: Item) => (
    <a
      key={place}
      href={placePaths[place]}
      className="shell-item"
      aria-current={active === place ? "page" : undefined}
      onClick={(event) => {
        event.preventDefault();
        go(placePaths[place]);
      }}
    >
      {glyph}
      <span>{t(label)}</span>
      {counts?.[place] !== undefined && <span className="shell-count">{new Intl.NumberFormat(locale === "de" ? "de-DE" : "en-GB").format(counts[place])}</span>}
    </a>
  );

  const administration: Item[] = [
    ...(manager ? [{ place: "organisation", label: "organisation", icon: icons.organisation } as Item, { place: "members", label: "members", icon: icons.members } as Item] : []),
    { place: "contacts", label: "contacts", icon: icons.contacts },
    ...(manager ? [{ place: "connections", label: "connections", icon: icons.connections } as Item] : []),
    ...(current?.role === "owner" ? [{ place: "sign-in", label: "signIn", icon: icons.signIn } as Item] : []),
    ...(manager
      ? [{ place: "look", label: "look", icon: icons.look } as Item, { place: "messages", label: "messages", icon: icons.messages } as Item, { place: "domains", label: "domains", icon: icons.domains } as Item]
      : []),
    { place: "developers", label: "developers", icon: icons.developers },
  ];
  // Simple (W45) shows the essentials; a place opened by search or address still shows while it is open.
  const shown = view === "simple" ? administration.filter((each) => !advancedPlaces.has(each.place) || each.place === active) : administration;
  // This Askwerk is for installation administrators and always Advanced; a place of it opened by address still shows.
  const installation: Item[] =
    me.installationAdmin === true
      ? [
          { place: "organisations", label: "organisations", icon: icons.organisations },
          { place: "administrators", label: "administrators", icon: icons.administrators },
          { place: "installation", label: "installation", icon: icons.installation },
        ]
      : [];
  const installationShown = view === "simple" ? installation.filter((each) => each.place === active) : installation;
  const hidden = administration.length - shown.length + installation.length - installationShown.length;

  return (
    <div className={drawer ? "shell shell--open" : "shell"} lang={locale}>
      <section className="shell-top" aria-label="Askwerk">
        <button type="button" className="shell-top__menu" aria-label={t("menu")} aria-expanded={drawer} onClick={() => setDrawer(true)}>
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M4 7h16M4 12h16M4 17h16" />
          </svg>
        </button>
        <span className="shell-logo">
          <Logo />
        </span>
        <span className="shell-top__org">{current?.name}</span>
      </section>
      {drawer && <button type="button" className="shell-scrim" aria-label={t("closeMenu")} onClick={() => setDrawer(false)} />}
      <aside className="shell-side" aria-label={t("navigation")}>
        {/* As tall as the page bar, so their bottom lines meet (W39). */}
        <div className="shell-side__head">
          <a
            href="/"
            className="shell-logo"
            onClick={(event) => {
              event.preventDefault();
              go("/");
            }}
          >
            <Logo />
          </a>
        </div>
        <Menu
          label={a("organisation")}
          trigger={(props) => (
            <button type="button" className="shell-org" aria-label={a("organisationMenu", { name: current?.name ?? "" })} {...props}>
              <span className="shell-org__mark" aria-hidden="true">
                {initialsOf(current?.name ?? "?")}
              </span>
              <span className="shell-org__name">{current?.name}</span>
              <svg className="shell-org__caret" viewBox="0 0 24 24" aria-hidden="true">
                <path d="M7 10l5 5 5-5" />
              </svg>
            </button>
          )}
        >
          <div className="shell-menu__head" role="presentation">
            {t("yourOrganisations")}
          </div>
          {me.organisations.map((organisation) => (
            <MenuItem
              key={organisation.id}
              disabled={organisation.status === "suspended"}
              hint={organisation.id === me.currentOrganisationId ? "✓" : undefined}
              onSelect={() => {
                if (organisation.id !== me.currentOrganisationId) {
                  // Another organisation always opens on its survey list (W29): switching reloads at the start page.
                  void client.switchOrganisation(organisation.id).then(onSwitched);
                }
              }}
            >
              <span className="shell-menu__org">
                <span>{organisation.name}</span>
                <span className="shell-menu__role">{organisation.status === "suspended" ? t("suspended") : roleName(a, organisation.role)}</span>
              </span>
            </MenuItem>
          ))}
          {manager && <MenuItem onSelect={() => go(placePaths.organisation)}>{t("organisationSettings")}</MenuItem>}
          {me.installationAdmin === true && <MenuItem onSelect={() => go(`${placePaths.organisations}?new`)}>{t("newOrganisation")}</MenuItem>}
          {onLeave !== undefined && current !== undefined && <MenuItem onSelect={() => setLeaving("ask")}>{a("leave")}</MenuItem>}
        </Menu>
        <nav className="shell-nav" aria-label={t("navigation")}>
          {survey !== undefined ? (
            <>
              <a
                href={inTemplate ? placePaths.templates : "/"}
                className="shell-item shell-back"
                onClick={(event) => {
                  event.preventDefault();
                  setDrawer(false);
                  if (inTemplate) {
                    onNavigate(placePaths.templates);
                  } else {
                    survey.onBack();
                  }
                }}
              >
                {icons.back}
                <span>{t(inTemplate ? "allTemplates" : "allSurveys")}</span>
              </a>
              <div ref={setSlot} className="shell-survey" />
            </>
          ) : (
            <>
              {item({ place: "surveys", label: "surveys", icon: icons.surveys })}
              {item({ place: "templates", label: "templates", icon: icons.templates })}
              <h2 className="shell-group">{t("administration")}</h2>
              {shown.map(item)}
              {hidden > 0 && (
                <button type="button" className="shell-more" onClick={() => setView("advanced")}>
                  <span>{hidden === 1 ? t("morePlace") : t("morePlaces", { count: hidden })}</span> · <b>{t("switchToAdvanced")}</b>
                </button>
              )}
              {installationShown.length > 0 && (
                <>
                  <h3 className="shell-group shell-group--sub">{t("thisAskwerk")}</h3>
                  {installationShown.map(item)}
                </>
              )}
            </>
          )}
        </nav>
        <Menu
          label={a("account")}
          alignEnd={false}
          className="shell-me-menu"
          trigger={(props) => (
            <button type="button" className="shell-me" aria-label={a("accountMenu", { name: me.name })} {...props}>
              <span className="shell-me__avatar" aria-hidden="true">
                {initialsOf(me.name)}
              </span>
              <span className="shell-me__who">
                <b>{me.name}</b>
                <span>{me.email}</span>
              </span>
            </button>
          )}
        >
          <div className="shell-menu__head" role="presentation">
            {t("view")}
          </div>
          <MenuItemRadio checked={view === "simple"} onSelect={() => setView("simple")}>
            {t("viewSimple")}
          </MenuItemRadio>
          <MenuItemRadio checked={view === "advanced"} onSelect={() => setView("advanced")}>
            {t("viewAdvanced")}
          </MenuItemRadio>
          <MenuSeparator />
          <MenuItem onSelect={() => (setDrawer(false), onOpenAccount())}>{a("accountAndSecurity")}</MenuItem>
          <MenuItem onSelect={() => void client.signOut().then(onSignedOut)}>{a("signOut")}</MenuItem>
        </Menu>
      </aside>
      <div className="shell-main">
        <header ref={setBarSlot} className="shell-bar" />
        <BarNavigate.Provider value={go}>
          <BarLocale.Provider value={locale}>
          <BarSlot.Provider value={barSlot}>
            <ShellTemplate.Provider value={setInTemplate}>
              <ShellView.Provider value={{ view, setView }}>
                <ShellSlot.Provider value={survey !== undefined ? slot : null}>{children}</ShellSlot.Provider>
              </ShellView.Provider>
            </ShellTemplate.Provider>
          </BarSlot.Provider>
          </BarLocale.Provider>
        </BarNavigate.Provider>
      </div>
      {note !== null && (
        <div className="shell-note" role="status">
          <span>{t(note.view === "advanced" ? "noteAdvanced" : "noteSimple")}</span>
          <button type="button" onClick={() => setView(note.before, true)}>
            {t("undo")}
          </button>
        </div>
      )}
      {leaving === "ask" && onLeave !== undefined && (
        <ConfirmDialog
          title={a("leaveTitle")}
          text={a("leaveAsk", { organisation: current?.name ?? "" })}
          confirm={a("leaveButton")}
          cancel={a("leaveCancel")}
          onCancel={() => setLeaving(null)}
          onConfirm={() => void onLeave().then(() => setLeaving(null), () => setLeaving("refused"))}
        />
      )}
      {leaving === "refused" && (
        <Dialog title={a("leaveTitle")} onClose={() => setLeaving(null)}>
          <p role="alert">{a("leaveRefused")}</p>
          <DialogActions>
            <button type="button" className="auth__button" autoFocus onClick={() => setLeaving(null)}>
              {a("leaveCancel")}
            </button>
          </DialogActions>
        </Dialog>
      )}
    </div>
  );
}
