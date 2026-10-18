// SPDX-License-Identifier: AGPL-3.0-only
import "./account.css";
import { ApiError, type AuthClient, type SessionState, type SetupEmail } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, LanguageMenu } from "@askwerk/ui";
import { useId, useState, type ReactNode } from "react";
import { Logo } from "../designer/Logo";
import { accountMessages, type AccountMessageKey } from "./messages";

type SetupWizardProps = {
  client: AuthClient;
  locale: Locale;
  baseAddress: string;
  timeZone: string;
  onDone: (state: SessionState) => void;
  onLocaleChange?: (locale: Locale) => void;
};

const slugPattern = /^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9]))*[a-z0-9]$/;

type Errors = Partial<Record<"name" | "email" | "password" | "repeat" | "instanceName" | "organisationName" | "slug", AccountMessageKey>>;

/** First-run setup (AWK-16 hi-fi M2): account, instance, organisation, optional email; everything is sent at the end. */
export function SetupWizard({ client, locale, baseAddress, timeZone, onDone, onLocaleChange }: SetupWizardProps) {
  const t = createTranslator(accountMessages, locale);
  const id = useId();
  const [step, setStep] = useState(0);
  const [errors, setErrors] = useState<Errors>({});
  const [suggestion, setSuggestion] = useState<string | null>(null);
  const [owner, setOwner] = useState({ name: "", email: "", password: "", repeat: "" });
  const [instance, setInstance] = useState({ name: "", baseAddress, defaultLanguage: locale as string, timeZone });
  const [organisation, setOrganisation] = useState({ name: "", slug: "" });
  const [smtp, setSmtp] = useState({ host: "", port: "587", userName: "", password: "", sender: "" });
  const [test, setTest] = useState<"idle" | "sent" | "failed">("idle");
  const [busy, setBusy] = useState(false);

  const smtpSettings = (): SetupEmail => ({
    host: smtp.host.trim(),
    port: Number(smtp.port) || 587,
    userName: smtp.userName.trim() === "" ? null : smtp.userName.trim(),
    password: smtp.password === "" ? null : smtp.password,
    sender: smtp.sender.trim(),
  });

  function validate(): Errors {
    const found: Errors = {};

    if (step === 0) {
      if (owner.name.trim() === "") found.name = "required";
      if (!owner.email.includes("@")) found.email = "required";
      if (owner.password.length < 12) found.password = "passwordHint";
      if (owner.password !== owner.repeat) found.repeat = "passwordsDiffer";
    } else if (step === 1) {
      if (instance.name.trim() === "") found.instanceName = "required";
    } else if (step === 2) {
      if (organisation.name.trim() === "") found.organisationName = "required";
      if (!slugPattern.test(organisation.slug) || organisation.slug.length > 40) found.slug = "slugInvalid";
    }

    return found;
  }

  async function next() {
    const found = validate();
    setErrors(found);

    if (Object.keys(found).length > 0) {
      return;
    }

    if (step === 2) {
      const check = await client.checkSlug(organisation.name, organisation.slug);

      if (!check.available) {
        setErrors({ slug: "slugTaken" });
        setSuggestion(check.suggestion);
        return;
      }
    }

    setSuggestion(null);
    setStep(step + 1);
  }

  async function sendTest() {
    try {
      await client.sendTestEmail(smtpSettings(), owner.email, locale);
      setTest("sent");
    } catch {
      setTest("failed");
    }
  }

  async function finish(withEmail: boolean) {
    setBusy(true);

    try {
      const session = await client.completeSetup({
        owner: { name: owner.name.trim(), email: owner.email.trim(), password: owner.password },
        instance,
        organisation: { name: organisation.name.trim(), slug: organisation.slug },
        ...(withEmail && smtp.host.trim() !== "" && { email: smtpSettings() }),
      });
      onDone(session.state);
    } catch (error) {
      const fields = error instanceof ApiError ? (error.problem.errors ?? {}) : {};

      if (fields.slug) {
        setStep(2);
        setErrors({ slug: fields.slug[0] === "slug-taken" ? "slugTaken" : "slugInvalid" });
      } else if (fields.password) {
        setStep(0);
        setErrors({ password: "passwordHint" });
      } else {
        setErrors({});
        setTest("failed");
      }
    } finally {
      setBusy(false);
    }
  }

  const field = (name: keyof Errors | "plain", label: string, input: ReactNode, hint?: string) => {
    const error = name === "plain" ? undefined : errors[name];

    return (
      <div className="auth__field">
        <label className="auth__label" id={`${id}-${label}-label`} htmlFor={`${id}-${label}`}>
          {label}
        </label>
        {input}
        {error !== undefined && error !== "passwordHint" && (
          <span id={`${id}-${label}-error`} className="auth__error">
            {t(error, { slug: organisation.slug })}
          </span>
        )}
        {hint !== undefined && <span className={error === "passwordHint" ? "auth__error" : "auth__hint"}>{hint}</span>}
      </div>
    );
  };

  const input = (label: string, value: string, onChange: (value: string) => void, options: { type?: string; autoComplete?: string; invalid?: boolean } = {}) => (
    <input
      id={`${id}-${label}`}
      className="auth__input"
      type={options.type ?? "text"}
      autoComplete={options.autoComplete}
      value={value}
      aria-invalid={options.invalid === true}
      onChange={(event) => onChange(event.target.value)}
    />
  );

  const steps: [AccountMessageKey, AccountMessageKey][] = [
    ["stepAccount", "stepAccountSub"],
    ["stepInstance", "stepInstanceSub"],
    ["stepOrganisation", "stepOrganisationSub"],
    ["stepEmail", "stepEmailSub"],
  ];

  return (
    <div className="wizard" lang={locale}>
      <header className="wizard__top">
        <Logo />
        <span className="auth__kicker">{t("setup")}</span>
        <span className="wizard__grow" />
        {onLocaleChange !== undefined && (
          <LanguageMenu label={t("language")} languages={["de", "en"]} value={locale} onChange={(language) => onLocaleChange(language as Locale)} />
        )}
      </header>
      <div className="wizard__body">
        <nav className="wizard__nav" aria-label={t("setupSteps")}>
        <ol className="wizard__steps" aria-label={t("setupSteps")}>
          {steps.map(([title, subtitle], index) => (
            <li
              key={title}
              className={index < step ? "wizard__step wizard__step--done" : index === step ? "wizard__step wizard__step--current" : "wizard__step"}
              aria-current={index === step ? "step" : undefined}
            >
              <b aria-hidden="true">{index < step ? "✓" : index + 1}</b>
              <span>
                {t(title)}
                <small>{t(subtitle)}</small>
              </span>
            </li>
          ))}
        </ol>
        </nav>
        <main className="wizard__main">
          <span className="auth__kicker">{t("stepOf", { number: step + 1 })}</span>
          {step === 0 && (
            <>
              <h1>{t("accountTitle")}</h1>
              <p className="auth__lead">{t("accountLead")}</p>
              <div className="wizard__row">
                {field("name", t("name"), input(t("name"), owner.name, (name) => setOwner({ ...owner, name }), { autoComplete: "name", invalid: errors.name !== undefined }))}
                {field("email", t("email"), input(t("email"), owner.email, (email) => setOwner({ ...owner, email }), { type: "email", autoComplete: "email", invalid: errors.email !== undefined }))}
              </div>
              {field(
                "password",
                t("password"),
                input(t("password"), owner.password, (password) => setOwner({ ...owner, password }), { type: "password", autoComplete: "new-password", invalid: errors.password !== undefined }),
                t("passwordHint"),
              )}
              {field(
                "repeat",
                t("repeatPassword"),
                input(t("repeatPassword"), owner.repeat, (repeat) => setOwner({ ...owner, repeat }), { type: "password", autoComplete: "new-password", invalid: errors.repeat !== undefined }),
              )}
              <div className="auth__alert auth__alert--warn">
                <b>{t("twoFactorRequiredTitle")}</b>
                <span>{t("twoFactorRequiredText")}</span>
              </div>
            </>
          )}
          {step === 1 && (
            <>
              <h1>{t("instanceTitle")}</h1>
              {field("instanceName", t("instanceName"), input(t("instanceName"), instance.name, (name) => setInstance({ ...instance, name }), { invalid: errors.instanceName !== undefined }))}
              {field("plain", t("baseAddress"), input(t("baseAddress"), instance.baseAddress, (address) => setInstance({ ...instance, baseAddress: address }), { type: "url" }), t("baseAddressHint"))}
              <div className="wizard__row">
                {field(
                  "plain",
                  t("defaultLanguage"),
                  <ComboBox
                    id={`${id}-${t("defaultLanguage")}`}
                    labelledBy={`${id}-${t("defaultLanguage")}-label`}
                    options={[
                      { value: "de", label: t("languageGerman") },
                      { value: "en", label: t("languageEnglish") },
                    ]}
                    value={instance.defaultLanguage}
                    onChange={(defaultLanguage) => setInstance({ ...instance, defaultLanguage })}
                    texts={{ choose: t("defaultLanguage") }}
                  />,
                )}
                {field("plain", t("timeZone"), input(t("timeZone"), instance.timeZone, (zone) => setInstance({ ...instance, timeZone: zone })))}
              </div>
            </>
          )}
          {step === 2 && (
            <>
              <h1>{t("organisationTitle")}</h1>
              <p className="auth__lead">{t("organisationLead")}</p>
              {field(
                "organisationName",
                t("organisationName"),
                input(t("organisationName"), organisation.name, (name) => setOrganisation({ ...organisation, name }), { invalid: errors.organisationName !== undefined }),
              )}
              {field(
                "slug",
                t("shortName"),
                input(t("shortName"), organisation.slug, (slug) => setOrganisation({ ...organisation, slug: slug.toLowerCase() }), { invalid: errors.slug !== undefined }),
                t("shortNameHint"),
              )}
              {suggestion !== null && (
                <span>
                  <button
                    type="button"
                    className="auth__link"
                    onClick={() => {
                      setOrganisation({ ...organisation, slug: suggestion });
                      setErrors({});
                      setSuggestion(null);
                    }}
                  >
                    {t("useSuggestion", { slug: suggestion })}
                  </button>
                </span>
              )}
            </>
          )}
          {step === 3 && (
            <>
              <h1>{t("emailTitle")}</h1>
              <p className="auth__lead">{t("emailLead")}</p>
              <div className="wizard__row wizard__row--wide">
                {field("plain", t("smtpServer"), input(t("smtpServer"), smtp.host, (host) => setSmtp({ ...smtp, host })))}
                {field("plain", t("port"), input(t("port"), smtp.port, (port) => setSmtp({ ...smtp, port }), { type: "number" }))}
              </div>
              <div className="wizard__row">
                {field("plain", t("user"), input(t("user"), smtp.userName, (userName) => setSmtp({ ...smtp, userName })))}
                {field("plain", t("smtpPassword"), input(t("smtpPassword"), smtp.password, (password) => setSmtp({ ...smtp, password }), { type: "password", autoComplete: "off" }))}
              </div>
              {field("plain", t("sender"), input(t("sender"), smtp.sender, (sender) => setSmtp({ ...smtp, sender })))}
              <div className="wizard__test">
                <button type="button" className="auth__button" onClick={() => void sendTest()} disabled={smtp.host.trim() === ""}>
                  {t("sendTestEmail")}
                </button>
                {test === "sent" && (
                  <span className="wizard__ok" role="status">
                    {t("testSent", { email: owner.email })}
                  </span>
                )}
                {test === "failed" && (
                  <span className="auth__error" role="alert">
                    {t("testFailed")}
                  </span>
                )}
              </div>
            </>
          )}
          <div className="wizard__actions">
            {step > 0 ? (
              <button type="button" className="auth__button" onClick={() => setStep(step - 1)}>
                {t("back")}
              </button>
            ) : (
              <span />
            )}
            {step < 3 ? (
              <button type="button" className="auth__button auth__button--primary" onClick={() => void next()}>
                {t("next")}
              </button>
            ) : (
              <span className="wizard__finish">
                <button type="button" className="auth__link" onClick={() => void finish(false)} disabled={busy}>
                  {t("skip")}
                </button>
                <button type="button" className="auth__button auth__button--primary" onClick={() => void finish(true)} disabled={busy}>
                  {t("finish")}
                </button>
              </span>
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
