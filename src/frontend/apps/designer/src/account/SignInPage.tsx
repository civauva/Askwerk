// SPDX-License-Identifier: AGPL-3.0-only
import { CodeInput } from "@askwerk/ui";
import { ApiError, type AuthClient, type SessionState, type SingleSignOnOptions } from "@askwerk/app-client";
import { createTranslator, type Locale, type Translate } from "@askwerk/schema";
import { useEffect, useId, useState, type FormEvent } from "react";
import { AuthLayout } from "./AuthLayout";
import { accountMessages, type AccountMessageKey } from "./messages";
import { browserPasskeys, type PasskeyDevice } from "./passkeys";

type SignInPageProps = {
  client: AuthClient;
  locale: Locale;
  instanceName: string;
  onDone: (state: SessionState) => void;
  onForgotPassword: () => void;
  onLocaleChange?: (locale: Locale) => void;
  now?: () => Date;
  timeZone?: string;
  /** The browser's passkeys; replaced in tests. */
  device?: PasskeyDevice;
  /** Opens an organisation's single sign-on (AWK-12); replaced in tests. */
  openSingleSignOn?: (organisation: string) => void;
  /** An organisation short name to start with, from /?sso= (a directory-only /sso link lands here). */
  initialSso?: string | null;
};

type Step =
  | { kind: "password"; error?: { triesLeft: number | null } | "other" | "passkey" }
  | { kind: "passkey"; error?: boolean }
  | { kind: "locked"; until: Date; traceId: string | undefined }
  | { kind: "code"; recovery: boolean; error?: boolean };

/** Sign-in (AWK-16 hi-fi M1): password, then the authenticator or a recovery code; wrong password and pause states. */
export function SignInPage({ client, locale, instanceName, onDone, onForgotPassword, onLocaleChange, now = () => new Date(), timeZone, device = browserPasskeys, openSingleSignOn = (organisation) => location.assign(`/sso/${encodeURIComponent(organisation)}`), initialSso = new URLSearchParams(location.search).get("sso") }: SignInPageProps) {
  const t = createTranslator(accountMessages, locale);
  const id = useId();
  const [step, setStep] = useState<Step>({ kind: "password" });
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [trust, setTrust] = useState(false);
  const [busy, setBusy] = useState(false);

  async function submitPassword(event: FormEvent) {
    event.preventDefault();
    setBusy(true);

    try {
      const session = await client.signIn(email, password);

      if (session.state === "two-factor-required") {
        setStep({ kind: "code", recovery: false });
      } else if (session.state === "passkey-required") {
        setStep({ kind: "passkey" });
      } else {
        onDone(session.state);
      }
    } catch (error) {
      if (error instanceof ApiError && error.problem.code === "locked" && error.problem.lockedUntil) {
        setStep({ kind: "locked", until: new Date(error.problem.lockedUntil), traceId: error.traceId });
      } else if (error instanceof ApiError && error.problem.code === "invalid-credentials") {
        setStep({ kind: "password", error: { triesLeft: error.problem.triesLeft ?? null } });
      } else {
        setStep({ kind: "password", error: "other" });
      }
    } finally {
      setBusy(false);
    }
  }

  /** W4 E: the passkey replaces email, password and app code; owners and admins without an app code use it as their second step. */
  async function usePasskey(afterPassword: boolean) {
    setBusy(true);

    try {
      const credential = await device.get(await client.passkeyRequestOptions());
      const session = await client.signInWithPasskey(credential);
      onDone(session.state);
    } catch {
      setStep(afterPassword ? { kind: "passkey", error: true } : { kind: "password", error: "passkey" });
    } finally {
      setBusy(false);
    }
  }

  async function submitCode(event: FormEvent, recovery: boolean) {
    event.preventDefault();
    setBusy(true);

    try {
      const session = recovery ? await client.submitRecoveryCode(code) : await client.submitTwoFactorCode(code.replace(/\s/g, ""), trust);
      onDone(session.state);
    } catch {
      setStep({ kind: "code", recovery, error: true });
    } finally {
      setBusy(false);
    }
  }

  if (step.kind === "locked") {
    const minutes = Math.max(1, Math.ceil((step.until.getTime() - now().getTime()) / 60_000));
    const elapsed = 1 - Math.min(1, minutes / 15);
    const time = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit", hourCycle: "h23", ...(timeZone !== undefined && { timeZone }) }).format(step.until);

    return (
      <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
        <span className="auth__kicker">{email}</span>
        <h1>{t("pausedTitle")}</h1>
        <p className="auth__lead">{t("pausedText")}</p>
        <div className="auth__field">
          <span className="auth__label">{t("possibleAgain")}</span>
          <span className="auth__clock">{time}</span>
          <div className="auth__progress" aria-hidden="true">
            <i style={{ width: `${elapsed * 100}%` }} />
          </div>
          <span className="auth__hint">{minutes === 1 ? t("minuteLeft") : t("minutesLeft", { count: minutes })}</span>
        </div>
        <button type="button" className="auth__button" onClick={onForgotPassword}>
          {t("resetPassword")}
        </button>
        {step.traceId !== undefined && <span className="auth__small auth__mono">{t("reference", { traceId: step.traceId })}</span>}
      </AuthLayout>
    );
  }

  if (step.kind === "passkey") {
    return (
      <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
        <span className="auth__kicker">{email}</span>
        <h1>{t("passkeyRequiredTitle")}</h1>
        <p className="auth__lead">{t("passkeyRequiredLead")}</p>
        {step.error && (
          <div className="auth__alert" role="alert">
            <b>{t("passkeyFailed")}</b>
          </div>
        )}
        <button type="button" className="auth__button auth__button--primary" disabled={busy} onClick={() => void usePasskey(true)}>
          {t("useYourPasskey")}
        </button>
        <span className="auth__links">
          <button type="button" className="auth__link" onClick={() => setStep({ kind: "password" })}>
            {t("backToSignIn")}
          </button>
        </span>
      </AuthLayout>
    );
  }

  if (step.kind === "code") {
    const recovery = step.recovery;

    return (
      <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
        <form className="auth__stack" onSubmit={(event) => void submitCode(event, recovery)} noValidate>
          <span className="auth__kicker">{email}</span>
          <h1>{t("codeTitle")}</h1>
          {!recovery && <p className="auth__lead">{t("codeLead")}</p>}
          <div className="auth__field">
            <label id={`${id}-code-label`} className="auth__label" htmlFor={recovery ? `${id}-code` : undefined}>
              {recovery ? t("recoveryLabel") : t("codeLabel")}
            </label>
            {recovery ? (
              <input
                id={`${id}-code`}
                key="recovery"
                className="auth__input auth__mono"
                inputMode="text"
                autoComplete="one-time-code"
                maxLength={20}
                autoFocus
                value={code}
                aria-invalid={step.error === true}
                aria-describedby={step.error ? `${id}-code-error` : undefined}
                onChange={(event) => setCode(event.target.value)}
              />
            ) : (
              <CodeInput
                labelledBy={`${id}-code-label`}
                value={code}
                onChange={setCode}
                digitLabel={(n) => t("digitOf", { n })}
                invalid={step.error === true}
                autoFocus
                describedBy={step.error ? `${id}-code-error` : undefined}
              />
            )}
            {step.error && (
              <span id={`${id}-code-error`} className="auth__error">
                {recovery ? t("recoveryInvalid") : t("codeInvalid")}
              </span>
            )}
          </div>
          {!recovery && (
            <label className="auth__check">
              <input type="checkbox" checked={trust} onChange={(event) => setTrust(event.target.checked)} />
              {t("trustBrowser")}
            </label>
          )}
          <button type="submit" className="auth__button auth__button--primary" disabled={busy}>
            {t("confirm")}
          </button>
          <span className="auth__links">
            <button
              type="button"
              className="auth__link"
              onClick={() => {
                setCode("");
                setStep({ kind: "code", recovery: !recovery });
              }}
            >
              {recovery ? t("useApp") : t("useRecovery")}
            </button>
            <button type="button" className="auth__link" onClick={() => setStep({ kind: "password" })}>
              {t("backToSignIn")}
            </button>
          </span>
        </form>
      </AuthLayout>
    );
  }

  const error = step.error;

  return (
    <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
      <form className="auth__stack" onSubmit={(event) => void submitPassword(event)} noValidate>
        <span className="auth__kicker">{instanceName}</span>
        <h1>{t("signIn")}</h1>
        {error !== undefined && (
          <div className="auth__alert" role="alert">
            {error === "other" ? (
              <b>{t("somethingWrong")}</b>
            ) : error === "passkey" ? (
              <>
                <b>{t("passkeyFailed")}</b>
                <span>{t("passkeyFailedHint")}</span>
              </>
            ) : (
              <>
                <b>{t("wrongCredentials")}</b>
                {error.triesLeft !== null && <span>{error.triesLeft === 1 ? t("triesLeftOne") : t("triesLeft", { count: error.triesLeft })}</span>}
              </>
            )}
          </div>
        )}
        <div className="auth__field">
          <label className="auth__label" htmlFor={`${id}-email`}>
            {t("email")}
          </label>
          <input
            id={`${id}-email`}
            className="auth__input"
            type="email"
            autoComplete="username"
            autoFocus
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </div>
        <div className="auth__field">
          <span className="auth__label-row">
            <label className="auth__label" htmlFor={`${id}-password`}>
              {t("password")}
            </label>
            <button type="button" className="auth__link" onClick={onForgotPassword}>
              {t("forgotPassword")}
            </button>
          </span>
          <input
            id={`${id}-password`}
            className="auth__input"
            type="password"
            autoComplete="current-password"
            value={password}
            aria-invalid={error !== undefined && error !== "other" && error !== "passkey"}
            onChange={(event) => setPassword(event.target.value)}
          />
        </div>
        <button type="submit" className="auth__button auth__button--primary" disabled={busy}>
          {t("signIn")}
        </button>
        {device.supported && (
          <button type="button" className="auth__button" disabled={busy} onClick={() => void usePasskey(false)}>
            {t("signInWithPasskey")}
          </button>
        )}
      </form>
      <SingleSignOn client={client} t={t} onDone={onDone} openSingleSignOn={openSingleSignOn} initial={initialSso} />
    </AuthLayout>
  );
}

type SingleSignOnProps = {
  client: AuthClient;
  t: Translate<AccountMessageKey>;
  onDone: (state: SessionState) => void;
  openSingleSignOn: (organisation: string) => void;
  initial: string | null;
};

/**
 * Sign-in for organisations with their own sign-in (AWK-12): their short name first; then single sign-on opens, or
 * the directory's user name and password are asked here. Folded away until asked for.
 */
function SingleSignOn({ client, t, onDone, openSingleSignOn, initial }: SingleSignOnProps) {
  const id = useId();
  const [organisation, setOrganisation] = useState<string | null>(initial ?? null);
  const [options, setOptions] = useState<SingleSignOnOptions | null>(null);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const look = (name: string) => {
    setProblem(null);
    client.singleSignOnOptions(name).then(
      (found) => {
        if (found.oidc && !found.ldap) {
          openSingleSignOn(name);
          return;
        }

        setOptions(found);
      },
      () => setProblem("ssoNotFound"),
    );
  };

  // A directory-only /sso link brings its organisation along.
  useEffect(() => {
    if (initial) {
      look(initial.toLowerCase());
    }
  }, []);

  if (organisation === null) {
    return (
      <button type="button" className="auth__link" onClick={() => setOrganisation("")}>
        {t("singleSignOn")}
      </button>
    );
  }

  const name = organisation.trim().toLowerCase();

  if (options === null) {
    return (
      <form
        className="auth__form"
        onSubmit={(event) => {
          event.preventDefault();
          if (name !== "") {
            look(name);
          }
        }}
      >
        <div className="auth__field">
          <label className="auth__label" htmlFor={`${id}-sso`}>
            {t("singleSignOnOrganisation")}
          </label>
          <input id={`${id}-sso`} className="auth__input" autoComplete="organization" value={organisation} onChange={(event) => setOrganisation(event.target.value)} aria-describedby={`${id}-sso-hint`} />
          <span id={`${id}-sso-hint`} className="auth__hint">
            {t("singleSignOnHint")}
          </span>
        </div>
        {problem !== null && <p role="alert">{t(problem as "ssoNotFound")}</p>}
        <button type="submit" className="auth__button">
          {t("singleSignOnContinue")}
        </button>
      </form>
    );
  }

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setProblem(null);
    client.signInWithDirectory(name, username.trim(), password).then(
      (session) => onDone(session.state),
      (failure: unknown) => {
        setBusy(false);
        const code = failure instanceof ApiError ? failure.problem.code : undefined;
        setProblem(code === "invalid-credentials" ? "directoryInvalid" : code === "no-access" ? "directoryNoAccess" : code === "no-email" ? "directoryNoEmail" : code === "locked" ? "directoryLocked" : "directoryUnreachable");
      },
    );
  };

  return (
    <>
      {options.oidc && (
        <button type="button" className="auth__button" onClick={() => openSingleSignOn(name)}>
          {t("signInWith", { name: options.oidcName ?? name })}
        </button>
      )}
      <form className="auth__form" onSubmit={submit} aria-labelledby={`${id}-directory`}>
        <h2 id={`${id}-directory`} className="auth__subtitle">
          {t("signInWith", { name: options.ldapName ?? name })}
        </h2>
        <div className="auth__field">
          <label className="auth__label" htmlFor={`${id}-user`}>
            {t("directoryUsername")}
          </label>
          <input id={`${id}-user`} className="auth__input" autoComplete="username" value={username} onChange={(event) => setUsername(event.target.value)} />
        </div>
        <div className="auth__field">
          <label className="auth__label" htmlFor={`${id}-password`}>
            {t("directoryPassword")}
          </label>
          <input id={`${id}-password`} className="auth__input" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} />
        </div>
        {problem !== null && <p role="alert">{t(problem as "directoryInvalid")}</p>}
        <button type="submit" className="auth__button auth__button--primary" disabled={busy || username.trim() === "" || password === ""}>
          {t("signIn")}
        </button>
      </form>
    </>
  );
}
