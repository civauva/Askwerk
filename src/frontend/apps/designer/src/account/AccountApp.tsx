// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type AuthClient, type Me, type MembersClient, type Theme } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { Skeleton } from "@askwerk/ui";
import { useCallback, useEffect, useState, type ComponentType } from "react";
import { AccountSecurityPage } from "./AccountSecurityPage";
import { AppShell } from "../shell/AppShell";
import { JoinPage } from "../members/JoinPage";
import { AuthLayout } from "./AuthLayout";
import { ChooseOrganisationPage } from "./ChooseOrganisationPage";
import { accountMessages } from "./messages";
import { browserPasskeys, type PasskeyDevice } from "./passkeys";
import { ResetPasswordPage } from "./ResetPasswordPage";
import { SetupWizard } from "./SetupWizard";
import { SignInPage } from "./SignInPage";
import { TwoFactorSetupPage } from "./TwoFactorSetupPage";

export type HomeProps = {
  me: Me;
  locale: Locale;
  client: AuthClient;
  /** Where in the app the signed-in user is, e.g. "/" (the survey list) or "/surveys/{id}". */
  path: string;
  onNavigate: (path: string) => void;
  onSignedOut: () => void;
  onSwitched: () => void;
  onOpenAccount: () => void;
};

type AccountAppProps = {
  client: AuthClient;
  initialLocale: Locale;
  location: { pathname: string; search: string };
  navigate: (path: string) => void;
  /** What a fully signed-in member sees; the designer once surveys are served (AWK-15, step 3). */
  home: ComponentType<HomeProps>;
  baseAddress?: string;
  timeZone?: string;
  /** Per-browser preferences (the organisation to open directly); browser storage by default. */
  storage?: PreferenceStorage;
  /** The browser's passkeys; replaced in tests. */
  device?: PasskeyDevice;
  /** Invitations into an organisation (AWK-30): /join/{token} opens when given. */
  members?: MembersClient;
};

export interface PreferenceStorage {
  get(key: string): string | null;
  set(key: string, value: string): void;
}

const rememberedOrganisationKey = "askwerk.organisation";

/** localStorage, but never failing: private windows and blocked storage simply remember nothing. */
export const browserStorage: PreferenceStorage = {
  get: (key) => {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  set: (key, value) => {
    try {
      localStorage.setItem(key, value);
    } catch {
      // Nothing to remember then.
    }
  },
};

type View =
  | { kind: "loading" }
  | { kind: "error"; traceId: string | undefined }
  | { kind: "starting" }
  | { kind: "setup" }
  | { kind: "sign-in"; joinToken?: string }
  | { kind: "join"; token: string }
  | { kind: "reset"; link?: { email: string; token: string } }
  | { kind: "two-factor-setup" }
  | { kind: "no-access"; me: Me }
  | { kind: "choose"; me: Me }
  | { kind: "home"; me: Me }
  | { kind: "account"; me: Me };

const accountPath = "/account";

/** The theme saved with the account (W4, section 4); "system" follows the operating system. */
function applyTheme(theme: string) {
  if (theme === "light" || theme === "dark") {
    document.documentElement.dataset.theme = theme;
  } else {
    delete document.documentElement.dataset.theme;
  }
}

/** Decides which screen comes first (AWK-16): setup, sign-in, two-factor setup, organisation choice, no access, or the app. */
export function AccountApp({ client, initialLocale, location, navigate, home: Home, baseAddress = "", timeZone = "UTC", storage = browserStorage, device = browserPasskeys, members }: AccountAppProps) {
  const [locale, setLocale] = useState<Locale>(initialLocale);
  const [path, setPath] = useState(location.pathname);
  const [view, setView] = useState<View>({ kind: "loading" });
  const [instanceName, setInstanceName] = useState("");
  const t = createTranslator(accountMessages, locale);

  const resolve = useCallback(async () => {
    const parameters = new URLSearchParams(location.search);
    const email = parameters.get("email");
    const token = parameters.get("token");

    if (path === "/reset-password" && email !== null && token !== null) {
      setView({ kind: "reset", link: { email, token } });
      return;
    }

    const joining = /^\/join\/([A-Za-z0-9_-]+)$/.exec(path);

    if (joining !== null && members !== undefined) {
      setInstanceName((await client.instanceName().catch(() => null)) ?? "");
      setView({ kind: "join", token: joining[1]! });
      return;
    }

    if (await client.setupRequired()) {
      setView({ kind: "setup" });
      return;
    }

    let me = await client.me();
    const remembered = storage.get(rememberedOrganisationKey);

    if (me !== null && me.state === "signed-in" && !me.currentOrganisationId && me.organisations.some((organisation) => organisation.id === remembered)) {
      await client.switchOrganisation(remembered!);
      me = await client.me();
    }

    if (me === null) {
      setInstanceName((await client.instanceName().catch(() => null)) ?? "");
      setView(path === "/reset-password" ? { kind: "reset" } : { kind: "sign-in" });
    } else if (me.state === "two-factor-setup-required") {
      setView({ kind: "two-factor-setup" });
    } else if (me.organisations.length === 0) {
      setView({ kind: "no-access", me });
    } else if (me.currentOrganisationId === null || me.currentOrganisationId === undefined) {
      setView({ kind: "choose", me });
    } else {
      if (me.language === "de" || me.language === "en") {
        setLocale(me.language);
      }

      applyTheme(me.theme);
      setView({ kind: path === accountPath ? "account" : "home", me });
    }
  }, [client, path, location.search, storage, members]);

  // Any failure shows its reference; while the database is not ready (503) the page asks again every 5 seconds.
  const attempt = useCallback(() => {
    resolve().catch((error: unknown) =>
      setView(error instanceof ApiError && error.status === 503 ? { kind: "starting" } : { kind: "error", traceId: error instanceof ApiError ? error.traceId : undefined }),
    );
  }, [resolve]);

  useEffect(attempt, [attempt]);

  useEffect(() => {
    if (view.kind !== "starting") {
      return undefined;
    }

    const timer = setTimeout(attempt, 5000);

    return () => clearTimeout(timer);
  }, [view.kind, attempt]);

  // A new path resolves again through the effect above; the same path resolves again directly.
  const go = (to: string) => {
    navigate(to);
    setView({ kind: "loading" });

    if (to === path) {
      attempt();
    } else {
      setPath(to);
    }
  };
  const reload = () => go("/");
  // After joining, the session works in the new organisation; owners and admins set up two-factor first.
  const joined = (state: string) => (state === "two-factor-setup-required" ? (navigate("/"), setView({ kind: "two-factor-setup" })) : reload());
  const signOut = () => {
    void client.signOut().then(reload);
  };
  const common = { client, locale, onLocaleChange: setLocale };

  switch (view.kind) {
    case "loading":
      return (
        <AuthLayout locale={locale}>
          <div role="status" aria-busy="true" aria-label={t("loading")} className="auth__stack">
            <Skeleton width="12rem" height="2.5rem" />
            <Skeleton width="100%" height="2.75rem" />
            <Skeleton width="100%" height="2.75rem" />
          </div>
        </AuthLayout>
      );
    case "error":
      return (
        <AuthLayout locale={locale} onLocaleChange={setLocale}>
          <h1>{t("errorTitle")}</h1>
          <p className="auth__lead">{t("errorText")}</p>
          <div>
            <button type="button" className="auth__button auth__button--primary" onClick={reload}>
              {t("tryAgain")}
            </button>
          </div>
          {view.traceId !== undefined && <span className="auth__small auth__mono">{t("referenceColon", { traceId: view.traceId })}</span>}
        </AuthLayout>
      );
    case "starting":
      return (
        <AuthLayout locale={locale} onLocaleChange={setLocale}>
          <h1>{t("startingTitle")}</h1>
          <p className="auth__lead">{t("startingText")}</p>
        </AuthLayout>
      );
    case "setup":
      return <SetupWizard {...common} baseAddress={baseAddress} timeZone={timeZone} onDone={reload} />;
    case "join":
      return (
        <JoinPage
          members={members!}
          token={view.token}
          locale={locale}
          onLocaleChange={setLocale}
          instanceName={instanceName}
          onJoined={joined}
          onSignIn={() => setView({ kind: "sign-in", joinToken: view.token })}
          onBack={reload}
        />
      );
    case "sign-in":
      return (
        <SignInPage
          {...common}
          instanceName={instanceName}
          onDone={() => {
            const token = view.joinToken;

            if (token === undefined || members === undefined) {
              reload();
            } else {
              members.join(token).then(joined, reload);
            }
          }}
          onForgotPassword={() => {
            navigate("/reset-password");
            setView({ kind: "reset" });
          }}
        />
      );
    case "reset":
      return <ResetPasswordPage {...common} link={view.link} onBack={reload} />;
    case "two-factor-setup":
      return <TwoFactorSetupPage {...common} onDone={reload} />;
    case "no-access":
      return (
        <AuthLayout locale={locale} onLocaleChange={setLocale}>
          <span className="auth__kicker">{view.me.email}</span>
          <h1>{t("noAccessTitle")}</h1>
          <p className="auth__lead">{t("noAccessText", { email: view.me.email })}</p>
          <div>
            <button type="button" className="auth__button" onClick={signOut}>
              {t("signOut")}
            </button>
          </div>
        </AuthLayout>
      );
    case "choose":
      return (
        <ChooseOrganisationPage
          {...common}
          me={view.me}
          onDone={reload}
          onSignOut={signOut}
          onRemember={(organisationId) => storage.set(rememberedOrganisationKey, organisationId)}
        />
      );
    case "home":
      return (
        <Home
          me={view.me}
          locale={locale}
          client={client}
          path={path}
          onNavigate={(to) => {
            navigate(to);
            setPath(to);
          }}
          onSignedOut={reload}
          onSwitched={reload}
          onOpenAccount={() => go(accountPath)}
        />
      );
    case "account":
      return (
        <>
          <AppShell client={client} locale={locale} me={view.me} active={null} onNavigate={go} onSignedOut={reload} onSwitched={reload} onOpenAccount={() => go(accountPath)}>
          <AccountSecurityPage
            client={client}
            device={device}
            me={view.me}
            locale={locale}
            onLocaleChange={setLocale}
            onThemeChange={(theme: Theme) => applyTheme(theme)}
            onSessionChanged={(state) => (state === "two-factor-setup-required" ? setView({ kind: "two-factor-setup" }) : reload())}
            timeZone={timeZone}
          />
          </AppShell>
        </>
      );
  }
}
