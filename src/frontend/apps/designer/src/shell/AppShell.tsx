// SPDX-License-Identifier: AGPL-3.0-only
import "./shell.css";
import type { AuthClient, Me } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ConfirmDialog, Dialog, DialogActions, Menu, MenuItem } from "@askwerk/ui";
import { createContext, useContext, useState, type ReactNode } from "react";
import { roleName } from "../account/ChooseOrganisationPage";
import { accountMessages } from "../account/messages";
import { initialsOf } from "../account/ProfileMenu";
import { shellMessages, type ShellMessageKey } from "./messages";

/** A place in the sidebar (AWK-38, W29). */
export type Place = "surveys" | "templates" | "organisation" | "members" | "contacts" | "sign-in" | "look" | "messages" | "domains" | "developers" | "organisations" | "administrators" | "installation";

/** Where each place lives. */
export const placePaths: Record<Place, string> = {
  surveys: "/",
  templates: "/templates",
  organisation: "/organisation",
  members: "/members",
  contacts: "/contacts",
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

  if (path === "/api-keys") {
    return "developers";
  }

  const found = (Object.entries(placePaths) as [Place, string][]).find(([, at]) => at !== "/" && path === at);

  return found?.[0] ?? null;
}

/** Inside a survey the sidebar shows the survey's own places; the survey's screens put them here (AWK-38). */
const ShellSlot = createContext<HTMLElement | null>(null);

/** The element the open survey's places go into, or null outside the shell (the embedded designer, tests). */
export const useShellSlot = () => useContext(ShellSlot);

type Item = { place: Place; label: ShellMessageKey; icon: ReactNode };

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
  client: Pick<AuthClient, "signOut" | "switchOrganisation">;
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
  children: ReactNode;
};

/**
 * The app's frame (AWK-38, W29): a sidebar with the organisation, the places and Administration, the person at the
 * foot; inside a survey it steps into the survey. On phones the sidebar is a drawer behind a menu button.
 */
export function AppShell({ client, locale, me, active, onNavigate, onSignedOut, onSwitched, onOpenAccount, onLeave, survey, children }: AppShellProps) {
  const t = createTranslator(shellMessages, locale);
  const a = createTranslator(accountMessages, locale);
  const [slot, setSlot] = useState<HTMLElement | null>(null);
  const [drawer, setDrawer] = useState(false);
  const [leaving, setLeaving] = useState<"ask" | "refused" | null>(null);
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
    </a>
  );

  const administration: Item[] = [
    ...(manager ? [{ place: "organisation", label: "organisation", icon: icons.organisation } as Item, { place: "members", label: "members", icon: icons.members } as Item] : []),
    { place: "contacts", label: "contacts", icon: icons.contacts },
    ...(current?.role === "owner" ? [{ place: "sign-in", label: "signIn", icon: icons.signIn } as Item] : []),
    ...(manager
      ? [{ place: "look", label: "look", icon: icons.look } as Item, { place: "messages", label: "messages", icon: icons.messages } as Item, { place: "domains", label: "domains", icon: icons.domains } as Item]
      : []),
    { place: "developers", label: "developers", icon: icons.developers },
  ];

  return (
    <div className={drawer ? "shell shell--open" : "shell"} lang={locale}>
      <section className="shell-top" aria-label="Askwerk">
        <button type="button" className="shell-top__menu" aria-label={t("menu")} aria-expanded={drawer} onClick={() => setDrawer(true)}>
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M4 7h16M4 12h16M4 17h16" />
          </svg>
        </button>
        <span className="shell-logo">
          Ask<span>werk</span>
        </span>
        <span className="shell-top__org">{current?.name}</span>
      </section>
      {drawer && <button type="button" className="shell-scrim" aria-label={t("closeMenu")} onClick={() => setDrawer(false)} />}
      <aside className="shell-side" aria-label={t("navigation")}>
        <a
          href="/"
          className="shell-logo"
          onClick={(event) => {
            event.preventDefault();
            go("/");
          }}
        >
          Ask<span>werk</span>
        </a>
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
                href="/"
                className="shell-item shell-back"
                onClick={(event) => {
                  event.preventDefault();
                  setDrawer(false);
                  survey.onBack();
                }}
              >
                {icons.back}
                <span>{t("allSurveys")}</span>
              </a>
              <div ref={setSlot} className="shell-survey" />
            </>
          ) : (
            <>
              {item({ place: "surveys", label: "surveys", icon: icons.surveys })}
              {item({ place: "templates", label: "templates", icon: icons.templates })}
              <h2 className="shell-group">{t("administration")}</h2>
              {administration.map(item)}
              {me.installationAdmin === true && (
                <>
                  <h3 className="shell-group shell-group--sub">{t("thisAskwerk")}</h3>
                  {item({ place: "organisations", label: "organisations", icon: icons.organisations })}
                  {item({ place: "administrators", label: "administrators", icon: icons.administrators })}
                  {item({ place: "installation", label: "installation", icon: icons.installation })}
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
          <MenuItem onSelect={() => (setDrawer(false), onOpenAccount())}>{a("accountAndSecurity")}</MenuItem>
          <MenuItem onSelect={() => void client.signOut().then(onSignedOut)}>{a("signOut")}</MenuItem>
        </Menu>
      </aside>
      <div className="shell-main">
        <ShellSlot.Provider value={survey !== undefined ? slot : null}>{children}</ShellSlot.Provider>
      </div>
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
