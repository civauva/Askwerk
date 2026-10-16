// SPDX-License-Identifier: AGPL-3.0-only
import type { AuthClient, Me } from "@askwerk/app-client";
import { createTranslator, type Locale, type Translate } from "@askwerk/schema";
import { useState } from "react";
import { AuthLayout } from "./AuthLayout";
import { accountMessages, type AccountMessageKey } from "./messages";

type ChooseOrganisationPageProps = {
  client: AuthClient;
  locale: Locale;
  me: Me;
  onDone: () => void;
  onSignOut?: () => void;
  /** Called with the chosen organisation when "Open directly next time" is ticked. */
  onRemember?: (organisationId: string) => void;
  onLocaleChange?: (locale: Locale) => void;
};

/** Choose organisation (AWK-16 hi-fi M3): shown only to members of more than one. */
export function ChooseOrganisationPage({ client, locale, me, onDone, onSignOut, onRemember, onLocaleChange }: ChooseOrganisationPageProps) {
  const t = createTranslator(accountMessages, locale);
  const [remember, setRemember] = useState(false);

  return (
    <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
      <span className="auth__kicker">{t("chooseKicker", { email: me.email, count: me.organisations.length })}</span>
      <h1>{t("chooseTitle")}</h1>
      <div className="auth__orgs">
        {me.organisations.map((organisation) => (
          <button
            key={organisation.id}
            type="button"
            className="auth__org"
            onClick={() => {
              if (remember) {
                onRemember?.(organisation.id);
              }

              void client.switchOrganisation(organisation.id).then(onDone);
            }}
          >
            <span className="auth__org-mark" aria-hidden="true">
              {initials(organisation.name)}
            </span>
            <span className="auth__org-text">
              <b>{organisation.name}</b> <span>{roleName(t, organisation.role)}</span>
            </span>
            <span aria-hidden="true">›</span>
          </button>
        ))}
      </div>
      {onRemember !== undefined && (
        <label className="auth__check">
          <input type="checkbox" checked={remember} onChange={(event) => setRemember(event.target.checked)} />
          {t("openDirectly")}
        </label>
      )}
      {onSignOut !== undefined && (
        <span className="auth__links">
          <button type="button" className="auth__link" onClick={onSignOut}>
            {t("signOut")}
          </button>
        </span>
      )}
    </AuthLayout>
  );
}

export function roleName(t: Translate<AccountMessageKey>, role: string): string {
  const keys: Record<string, AccountMessageKey> = { owner: "roleOwner", admin: "roleAdmin", editor: "roleEditor", analyst: "roleAnalyst", viewer: "roleViewer" };

  return keys[role] !== undefined ? t(keys[role]) : role;
}

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter((word) => /^\p{L}/u.test(word))
    .slice(0, 2)
    .map((word) => word[0]!.toUpperCase())
    .join("");
}
