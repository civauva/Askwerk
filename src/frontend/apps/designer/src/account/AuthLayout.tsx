// SPDX-License-Identifier: AGPL-3.0-only
import "./account.css";
import { createTranslator, type Locale } from "@askwerk/schema";
import type { ReactNode } from "react";
import { Logo } from "../designer/Logo";
import { accountMessages } from "./messages";

type AuthLayoutProps = {
  locale: Locale;
  onLocaleChange?: ((locale: Locale) => void) | undefined;
  children: ReactNode;
};

/** The sign-in look (AWK-16 hi-fi M1): the form on paper, the workshop panel with the measuring rule on the right. */
export function AuthLayout({ locale, onLocaleChange, children }: AuthLayoutProps) {
  const t = createTranslator(accountMessages, locale);

  return (
    <div className="auth" lang={locale}>
      <div className="auth__form-side">
        <header className="auth__top">
          <Logo />
          {onLocaleChange !== undefined && (
            <span className="auth__segment" role="group" aria-label={t("language")}>
              {(["de", "en"] as const).map((language) => (
                <button key={language} type="button" aria-pressed={locale === language} onClick={() => onLocaleChange(language)}>
                  {language.toUpperCase()}
                </button>
              ))}
            </span>
          )}
        </header>
        <div className="auth__ruler" aria-hidden="true">
          <i />
        </div>
        <main className="auth__center">
          <div className="auth__form">{children}</div>
        </main>
        <footer className="auth__foot">
          <span>Askwerk</span>
          <span>{t("imprint")}</span>
          <span>{t("privacy")}</span>
        </footer>
      </div>
      <aside className="auth__side" aria-hidden="true">
        <div className="auth__side-rule">
          <i />
          {[0, 25, 50, 75, 100].map((mark) => (
            <span key={mark} style={{ left: `${mark}%` }}>
              {mark}
            </span>
          ))}
        </div>
        <span className="auth__meta">{t("sideMeta")}</span>
        <p className="auth__side-title">
          {t("sideTitle1")}
          <br />
          {t("sideTitle2Before")}
          <b>{t("sideTitle2Strong")}</b>
          {t("sideTitle2After")}
        </p>
        <p className="auth__side-text">{t("sideText")}</p>
      </aside>
    </div>
  );
}
