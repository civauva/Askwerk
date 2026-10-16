// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type AuthClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useEffect, useId, useState, type FormEvent } from "react";
import { AuthLayout } from "./AuthLayout";
import { accountMessages } from "./messages";

type ResetPasswordPageProps = {
  client: AuthClient;
  locale: Locale;
  /** Email and token from the link in the email; without it the page asks for the address. */
  link?: { email: string; token: string } | undefined;
  onBack: () => void;
  onLocaleChange?: (locale: Locale) => void;
};

type State = "loading" | "unavailable" | "ready" | "sent" | "changed";

/** Password reset (AWK-16 hi-fi M1 E): ask for the link, then set the new password from it. */
export function ResetPasswordPage({ client, locale, link, onBack, onLocaleChange }: ResetPasswordPageProps) {
  const t = createTranslator(accountMessages, locale);
  const id = useId();
  const [state, setState] = useState<State>(link === undefined ? "loading" : "ready");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<"token" | "password" | "other" | null>(null);

  useEffect(() => {
    if (link === undefined) {
      client.passwordResetAvailable().then(
        (available) => setState(available ? "ready" : "unavailable"),
        () => setState("ready"),
      );
    }
  }, [client, link]);

  async function sendLink(event: FormEvent) {
    event.preventDefault();
    await client.requestPasswordReset(email).catch(() => undefined);
    setState("sent");
  }

  async function setNewPassword(event: FormEvent) {
    event.preventDefault();

    try {
      await client.confirmPasswordReset(link!.email, link!.token, password);
      setState("changed");
    } catch (caught) {
      const errors = caught instanceof ApiError ? caught.problem.errors : undefined;
      setError(errors?.token ? "token" : errors?.password ? "password" : "other");
    }
  }

  return (
    <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
      <span className="auth__kicker">{link?.email ?? ""}</span>
      <h1>{t("resetPassword")}</h1>
      {state === "unavailable" && <p className="auth__lead">{t("resetUnavailable")}</p>}
      {(state === "ready" || state === "sent") && link === undefined && (
        <form className="auth__stack" onSubmit={(event) => void sendLink(event)} noValidate>
          <p className="auth__lead">{t("resetLead")}</p>
          <div className="auth__field">
            <label className="auth__label" htmlFor={`${id}-email`}>
              {t("email")}
            </label>
            <input id={`${id}-email`} className="auth__input" type="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} />
          </div>
          <button type="submit" className="auth__button auth__button--primary">
            {t("sendLink")}
          </button>
          {state === "sent" && (
            <div className="auth__alert auth__alert--ok" role="status">
              <b>{t("linkSent")}</b>
              <span>{t("linkSentSpam")}</span>
            </div>
          )}
        </form>
      )}
      {state === "ready" && link !== undefined && (
        <form className="auth__stack" onSubmit={(event) => void setNewPassword(event)} noValidate>
          <div className="auth__field">
            <label className="auth__label" htmlFor={`${id}-password`}>
              {t("newPassword")}
            </label>
            <input
              id={`${id}-password`}
              className="auth__input"
              type="password"
              autoComplete="new-password"
              value={password}
              aria-invalid={error === "password"}
              aria-describedby={`${id}-hint`}
              onChange={(event) => setPassword(event.target.value)}
            />
            <span id={`${id}-hint`} className={error === "password" ? "auth__error" : "auth__hint"}>
              {t("passwordHint")}
            </span>
          </div>
          {error === "token" && (
            <div className="auth__alert" role="alert">
              <b>{t("linkInvalid")}</b>
            </div>
          )}
          {error === "other" && (
            <div className="auth__alert" role="alert">
              <b>{t("somethingWrong")}</b>
            </div>
          )}
          <button type="submit" className="auth__button auth__button--primary">
            {t("setPassword")}
          </button>
        </form>
      )}
      {state === "changed" && (
        <div className="auth__alert auth__alert--ok" role="status">
          <b>{t("passwordChanged")}</b>
        </div>
      )}
      <span className="auth__links">
        <button type="button" className="auth__link" onClick={onBack}>
          {t("backToSignIn")}
        </button>
      </span>
    </AuthLayout>
  );
}
