// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type InvitationToJoin, type MembersClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { Skeleton } from "@askwerk/ui";
import { useEffect, useId, useState } from "react";
import { AuthLayout } from "../account/AuthLayout";
import { membersMessages, type MembersMessageKey } from "./messages";

type JoinPageProps = {
  members: MembersClient;
  token: string;
  locale: Locale;
  onLocaleChange?: (locale: Locale) => void;
  instanceName: string;
  /** Joined: the session state (signed-in, or two-factor-setup-required for owners and admins). */
  onJoined: (state: string) => void;
  /** The address has an account: sign in first, then join. */
  onSignIn: () => void;
  /** Back to sign-in from a link that no longer works. */
  onBack?: () => void;
};

type Loaded = { kind: "loading" } | { kind: "gone" } | { kind: "failed" } | { kind: "ready"; invitation: InvitationToJoin };

/** Opening an invitation link (AWK-30, W18 C): a new person sets name and password; an account joins as it is. */
export function JoinPage({ members, token, locale, onLocaleChange, onJoined, onSignIn, onBack }: JoinPageProps) {
  const t = createTranslator(membersMessages, locale);
  const id = useId();
  const [loaded, setLoaded] = useState<Loaded>({ kind: "loading" });
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<MembersMessageKey | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    members.invitation(token).then(
      (invitation) => setLoaded({ kind: "ready", invitation }),
      (failure: unknown) => setLoaded(failure instanceof ApiError && failure.status === 410 ? { kind: "gone" } : { kind: "failed" }),
    );
  }, [members, token]);

  const layout = { locale, ...(onLocaleChange === undefined ? {} : { onLocaleChange }) };

  if (loaded.kind === "loading") {
    return (
      <AuthLayout {...layout}>
        <div role="status" aria-busy="true" className="auth__stack">
          <Skeleton width="16rem" height="2.5rem" />
          <Skeleton width="100%" height="2.75rem" />
        </div>
      </AuthLayout>
    );
  }

  if (loaded.kind !== "ready") {
    return (
      <AuthLayout {...layout}>
        <h1>{loaded.kind === "gone" ? t("joinGoneTitle") : t("failed")}</h1>
        {loaded.kind === "gone" && <p className="auth__lead">{t("joinGoneText")}</p>}
        {onBack !== undefined && (
          <div>
            <button type="button" className="auth__button" onClick={onBack}>
              {t("joinToSignIn")}
            </button>
          </div>
        )}
      </AuthLayout>
    );
  }

  const { invitation } = loaded;
  const join = (account?: { name: string; password: string }) => {
    setBusy(true);
    setError(null);
    members.join(token, account).then(onJoined, (failure: unknown) => {
      setBusy(false);
      setError(failure instanceof ApiError && failure.status === 410 ? "joinGoneTitle" : failure instanceof ApiError && failure.problem.errors?.["password"] !== undefined ? "joinPasswordShort" : "failed");
    });
  };

  return (
    <AuthLayout {...layout}>
      <h1>{t("joinTitle", { organisation: invitation.organisation, role: t(`${invitation.role}Long` as MembersMessageKey) })}</h1>
      <p className="auth__lead">{t("joinInvitedBy", { name: invitation.invitedBy })}</p>
      {invitation.signedInAs !== null ? (
        <div className="auth__stack">
          <p>{t("joinSignedInAs", { email: invitation.signedInAs })}</p>
          {error !== null && <p className="auth__error" role="alert">{t(error)}</p>}
          <button type="button" className="auth__button auth__button--primary" disabled={busy} onClick={() => join()}>
            {t("joinButton", { organisation: invitation.organisation })}
          </button>
        </div>
      ) : invitation.accountExists ? (
        <div className="auth__stack">
          <p>{t("joinAccountExists", { email: invitation.email, organisation: invitation.organisation })}</p>
          <button type="button" className="auth__button auth__button--primary" onClick={onSignIn}>
            {t("joinSignIn")}
          </button>
        </div>
      ) : (
        <form
          className="auth__stack"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();

            if (name.trim() === "") {
              setError("joinNameRequired");
            } else if (password.length < 12) {
              setError("joinPasswordShort");
            } else {
              join({ name: name.trim(), password });
            }
          }}
        >
          {error !== null && (
            <p className="auth__error" role="alert">
              {t(error)}
            </p>
          )}
          <div className="auth__field">
            <label className="auth__label" htmlFor={`${id}-name`}>
              {t("joinName")}
            </label>
            <input id={`${id}-name`} className="auth__input" autoComplete="name" autoFocus value={name} onChange={(event) => setName(event.target.value)} />
          </div>
          <div className="auth__field">
            <label className="auth__label" htmlFor={`${id}-email`}>
              {t("joinEmail")}
            </label>
            <input id={`${id}-email`} className="auth__input" type="email" autoComplete="username" readOnly value={invitation.email} />
          </div>
          <div className="auth__field">
            <label className="auth__label" htmlFor={`${id}-password`}>
              {t("joinPassword")}
            </label>
            <input
              id={`${id}-password`}
              className="auth__input"
              type="password"
              autoComplete="new-password"
              aria-describedby={`${id}-password-hint`}
              aria-invalid={error === "joinPasswordShort"}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <span id={`${id}-password-hint`} className="auth__hint">
              {t("joinPasswordHint")}
            </span>
          </div>
          <button type="submit" className="auth__button auth__button--primary" disabled={busy}>
            {t("joinButton", { organisation: invitation.organisation })}
          </button>
        </form>
      )}
    </AuthLayout>
  );
}
