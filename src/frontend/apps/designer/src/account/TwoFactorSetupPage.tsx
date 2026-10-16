// SPDX-License-Identifier: AGPL-3.0-only
import type { AuthClient, TwoFactorSetup } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { CodeInput, QrCode } from "@askwerk/ui";
import { useEffect, useId, useState, type FormEvent } from "react";
import { AuthLayout } from "./AuthLayout";
import { accountMessages } from "./messages";

type TwoFactorSetupPageProps = {
  client: AuthClient;
  locale: Locale;
  onDone: () => void;
  onLocaleChange?: (locale: Locale) => void;
};

/** Required two-factor setup for owners and admins (AWK-16 hi-fi M3): QR code or key, a code, then the recovery codes once. */
export function TwoFactorSetupPage({ client, locale, onDone, onLocaleChange }: TwoFactorSetupPageProps) {
  const t = createTranslator(accountMessages, locale);
  const id = useId();
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null);
  const [code, setCode] = useState("");
  const [invalid, setInvalid] = useState(false);
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);

  useEffect(() => {
    client.twoFactorSetup().then(setSetup, () => undefined);
  }, [client]);

  async function confirm(event: FormEvent) {
    event.preventDefault();

    try {
      const enabled = await client.enableTwoFactor(code.replace(/\s/g, ""));
      setRecoveryCodes(enabled.recoveryCodes);
    } catch {
      setInvalid(true);
    }
  }

  if (recoveryCodes !== null) {
    return (
      <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
        <h1>{t("recoveryTitle")}</h1>
        <p className="auth__lead">{t("recoveryLead")}</p>
        <ul className="auth__codes">
          {recoveryCodes.map((recoveryCode) => (
            <li key={recoveryCode}>{recoveryCode}</li>
          ))}
        </ul>
        <button type="button" className="auth__button auth__button--primary" onClick={onDone}>
          {t("savedThem")}
        </button>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout locale={locale} onLocaleChange={onLocaleChange}>
      <form className="auth__stack" onSubmit={(event) => void confirm(event)} noValidate>
        <span className="auth__kicker">{t("twoFactorKicker")}</span>
        <h1>{t("twoFactorTitle")}</h1>
        <p className="auth__lead">{t("twoFactorLead")}</p>
        {setup !== null && (
          <div className="auth__qr">
            <QrCode value={setup.authenticatorUri} label={t("qrLabel")} />
            <div className="auth__field">
              <span className="auth__label">{t("keyLabel")}</span>
              <span className="auth__secret">{setup.sharedKey.match(/.{1,4}/g)?.join(" ")}</span>
              <span className="auth__hint">{t("appsHint")}</span>
            </div>
          </div>
        )}
        <div className="auth__field">
          <span id={`${id}-code-label`} className="auth__label">
            {t("codeLabel")}
          </span>
          <CodeInput labelledBy={`${id}-code-label`} value={code} onChange={setCode} digitLabel={(n) => t("digitOf", { n })} invalid={invalid} />
          {invalid && <span className="auth__error">{t("codeInvalid")}</span>}
        </div>
        <button type="submit" className="auth__button auth__button--primary">
          {t("confirmContinue")}
        </button>
      </form>
    </AuthLayout>
  );
}
