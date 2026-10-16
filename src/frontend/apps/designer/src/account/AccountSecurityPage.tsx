// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type AuthClient, type Me, type Passkey, type SessionState, type Theme } from "@askwerk/app-client";
import { createTranslator, pickLocale, type Locale } from "@askwerk/schema";
import { ComboBox, Dialog as SharedDialog } from "@askwerk/ui";
import { useCallback, useEffect, useId, useState, type FormEvent, type ReactNode } from "react";
import { roleName } from "./ChooseOrganisationPage";
import { accountMessages, type AccountMessageKey } from "./messages";
import type { PasskeyDevice } from "./passkeys";

type AccountSecurityPageProps = {
  client: AuthClient;
  device: PasskeyDevice;
  me: Me;
  locale: Locale;
  onLocaleChange: (locale: Locale) => void;
  onThemeChange: (theme: Theme) => void;
  /** After moving to a new phone (or turning two-factor on), the session needs the two-factor setup. */
  onSessionChanged: (state: SessionState) => void;
  now?: () => Date;
  timeZone?: string;
};

/** What the password confirmation is for (W4 D). */
type Confirm = { kind: "remove"; passkey: Passkey } | { kind: "codes" } | { kind: "phone" } | { kind: "off" };

const confirmText: Record<Confirm["kind"], [lead: AccountMessageKey, action: AccountMessageKey]> = {
  remove: ["confirmRemovePasskey", "remove"],
  codes: ["confirmNewCodes", "newRecoveryCodes"],
  phone: ["confirmNewPhone", "moveToNewPhone"],
  off: ["confirmTurnOff", "turnOff"],
};

/** Account and security (AWK-16, W4): passkeys, two-factor, password, preferences and sign out everywhere. */
export function AccountSecurityPage({ client, device, me, locale, onLocaleChange, onThemeChange, onSessionChanged, now = () => new Date(), timeZone }: AccountSecurityPageProps) {
  const t = createTranslator(accountMessages, locale);
  const id = useId();
  const [passkeys, setPasskeys] = useState<Passkey[] | null>(null);
  const [adding, setAdding] = useState<{ credential: string; name: string } | null>(null);
  const [addFailed, setAddFailed] = useState(false);
  const [renaming, setRenaming] = useState<{ id: string; name: string } | null>(null);
  const [confirm, setConfirm] = useState<Confirm | null>(null);
  const [twoFactorOn, setTwoFactorOn] = useState(me.twoFactorEnabled);
  const [codesLeft, setCodesLeft] = useState(me.recoveryCodesLeft);
  const [newCodes, setNewCodes] = useState<string[] | null>(null);
  const [language, setLanguage] = useState(me.language ?? "");
  const [theme, setTheme] = useState<Theme>((me.theme as Theme | undefined) ?? "system");
  const [signedOutElsewhere, setSignedOutElsewhere] = useState(false);
  const current = me.organisations.find((organisation) => organisation.id === me.currentOrganisationId) ?? me.organisations[0];

  const loadPasskeys = useCallback(() => {
    client.passkeys().then(setPasskeys, () => setPasskeys([]));
  }, [client]);

  useEffect(loadPasskeys, [loadPasskeys]);

  const dateFormat = new Intl.DateTimeFormat(locale === "en" ? "en-GB" : locale, { day: "numeric", month: "short", year: "numeric", ...(timeZone !== undefined && { timeZone }) });
  const day = (value: Date) => new Intl.DateTimeFormat("en-CA", { ...(timeZone !== undefined && { timeZone }) }).format(value);
  const describe = (passkey: Passkey) => {
    const added = dateFormat.format(new Date(passkey.createdAt));

    if (passkey.lastUsedAt === null || passkey.lastUsedAt === undefined) {
      return t("passkeyNeverUsed", { date: added });
    }

    const used = new Date(passkey.lastUsedAt);

    return t("passkeyAdded", { date: added, used: day(used) === day(now()) ? t("today") : dateFormat.format(used) });
  };

  async function startAdding() {
    setAddFailed(false);

    try {
      const credential = await device.create(await client.passkeyCreationOptions());
      setAdding({ credential, name: "" });
    } catch {
      setAddFailed(true);
    }
  }

  async function saveAdded(event: FormEvent) {
    event.preventDefault();

    if (adding === null) {
      return;
    }

    try {
      await client.addPasskey(adding.credential, adding.name.trim() || null);
    } catch {
      setAddFailed(true);
    }

    setAdding(null);
    loadPasskeys();
  }

  async function saveRename(event: FormEvent) {
    event.preventDefault();

    if (renaming === null) {
      return;
    }

    await client.renamePasskey(renaming.id, renaming.name.trim() || null).catch(() => undefined);
    setRenaming(null);
    loadPasskeys();
  }

  async function confirmed(password: string) {
    switch (confirm?.kind) {
      case "remove":
        await client.removePasskey(confirm.passkey.id, password);
        loadPasskeys();
        break;
      case "codes": {
        const codes = await client.newRecoveryCodes(password);
        setNewCodes(codes);
        setCodesLeft(codes.length);
        break;
      }
      case "phone":
        onSessionChanged((await client.resetTwoFactor(password)).state);
        break;
      case "off":
        await client.disableTwoFactor(password);
        setTwoFactorOn(false);
        break;
      case undefined:
        break;
    }

    setConfirm(null);
  }

  function savePreferences(nextLanguage: string, nextTheme: Theme) {
    setLanguage(nextLanguage);
    setTheme(nextTheme);
    onLocaleChange(nextLanguage === "de" || nextLanguage === "en" ? nextLanguage : pickLocale(navigator.languages));
    onThemeChange(nextTheme);
    void client.savePreferences(nextLanguage === "" ? null : nextLanguage, nextTheme).catch(() => undefined);
  }

  return (
    <main className="account" lang={locale}>
      <h1>{t("accountAndSecurity")}</h1>
      <p className="account__who">
        {t("accountWho", { name: me.name, email: me.email, role: current === undefined ? "" : roleName(t, current.role), organisation: current?.name ?? "" })}
      </p>

      <Section id={`${id}-passkeys`} number={1} title={t("passkeys")} aside={t("passkeysLead")}>
        {!device.supported && (
          <div className="auth__alert auth__alert--warn" role="status">
            <b>{t("passkeyNotSupported")}</b>
            <span>{t("passkeyNotSupportedHint")}</span>
          </div>
        )}
        {addFailed && (
          <div className="auth__alert" role="alert">
            <b>{t("passkeyNotAdded")}</b>
            <span>{t("passkeyNotAddedHint")}</span>
          </div>
        )}
        <ul className="account__list">
          {passkeys?.map((passkey) => {
            const name = passkey.name ?? t("unnamedPasskey");

            return (
              <li key={passkey.id} className="account__row">
                {renaming?.id === passkey.id ? (
                  <form className="account__inline" onSubmit={(event) => void saveRename(event)}>
                    <input
                      className="auth__input"
                      aria-label={t("nameOf", { name })}
                      autoFocus
                      maxLength={100}
                      value={renaming.name}
                      onChange={(event) => setRenaming({ id: passkey.id, name: event.target.value })}
                    />
                    <button type="submit" className="auth__button auth__button--primary">
                      {t("save")}
                    </button>
                    <button type="button" className="auth__button" onClick={() => setRenaming(null)}>
                      {t("cancel")}
                    </button>
                  </form>
                ) : (
                  <>
                    <span className="account__row-text">
                      <b>{name}</b>
                      <span>{describe(passkey)}</span>
                    </span>
                    <button type="button" className="auth__link" aria-label={t("renameNamed", { name })} onClick={() => setRenaming({ id: passkey.id, name: passkey.name ?? "" })}>
                      {t("rename")}
                    </button>
                    <button type="button" className="auth__link account__danger" aria-label={t("removeNamed", { name })} onClick={() => setConfirm({ kind: "remove", passkey })}>
                      {t("remove")}
                    </button>
                  </>
                )}
              </li>
            );
          })}
        </ul>
        <div className="account__actions">
          <button type="button" className="auth__button" disabled={!device.supported} onClick={() => void startAdding()}>
            <span aria-hidden="true">+ </span>
            {t("addPasskey")}
          </button>
          <span className="auth__hint">{t("addPasskeyHint")}</span>
        </div>
      </Section>

      <Section id={`${id}-two-factor`} number={2} title={t("twoFactorApp")} aside={t("twoFactorRequiredForRoles")}>
        <p className="account__status">
          <span className={twoFactorOn ? "account__badge account__badge--on" : "account__badge"}>{twoFactorOn ? (
              <>
                <span aria-hidden="true">✓ </span>
                {t("on")}
              </>
            ) : (
              t("off")
            )}</span>
          {twoFactorOn && <span>{t("recoveryCodesLeft", { count: codesLeft })}</span>}
        </p>
        {newCodes !== null && (
          <div className="auth__alert auth__alert--ok" role="status">
            <b>{t("newCodesTitle")}</b>
            <span>{t("newCodesLead")}</span>
            <ul className="auth__codes">
              {newCodes.map((code) => (
                <li key={code}>{code}</li>
              ))}
            </ul>
            <div>
              <button type="button" className="auth__button" onClick={() => setNewCodes(null)}>
                {t("done")}
              </button>
            </div>
          </div>
        )}
        <div className="account__actions">
          {twoFactorOn ? (
            <>
              <button type="button" className="auth__button" onClick={() => setConfirm({ kind: "codes" })}>
                {t("newRecoveryCodes")}
              </button>
              <button type="button" className="auth__button" onClick={() => setConfirm({ kind: "phone" })}>
                {t("moveToNewPhone")}
              </button>
              <button type="button" className="auth__button" disabled={me.twoFactorRequired} aria-describedby={me.twoFactorRequired ? `${id}-off-reason` : undefined} onClick={() => setConfirm({ kind: "off" })}>
                {t("turnOff")}
              </button>
            </>
          ) : (
            <button type="button" className="auth__button auth__button--primary" onClick={() => onSessionChanged("two-factor-setup-required")}>
              {t("turnOn")}
            </button>
          )}
        </div>
        {twoFactorOn && me.twoFactorRequired && (
          <span id={`${id}-off-reason`} className="auth__hint">
            {t("turnOffDisabled")}
          </span>
        )}
        <span className="auth__hint">{t("confirmFirst")}</span>
      </Section>

      <PasswordSection client={client} locale={locale} />

      <Section id={`${id}-preferences`} number={4} title={t("preferences")}>
        <div className="account__grid">
          <div className="auth__field">
            <span className="auth__label" id={`${id}-language-label`}>
              {t("language")}
            </span>
            <ComboBox
              id={`${id}-language`}
              labelledBy={`${id}-language-label`}
              options={[
                { value: "", label: t("browserLanguage") },
                { value: "de", label: "Deutsch" },
                { value: "en", label: "English" },
              ]}
              value={language}
              onChange={(value) => savePreferences(value, theme)}
              texts={{ choose: t("browserLanguage") }}
            />
          </div>
          <div className="auth__field">
            <span className="auth__label" id={`${id}-theme-label`}>
              {t("theme")}
            </span>
            <ComboBox
              id={`${id}-theme`}
              labelledBy={`${id}-theme-label`}
              options={[
                { value: "system", label: t("themeSystem") },
                { value: "light", label: t("themeLight") },
                { value: "dark", label: t("themeDark") },
              ]}
              value={theme}
              onChange={(value) => savePreferences(language, value as Theme)}
              texts={{ choose: t("themeSystem") }}
            />
          </div>
        </div>
        <span className="auth__hint">{t("savedAsYouChange")}</span>
      </Section>

      <section className="account__card account__card--row" aria-labelledby={`${id}-everywhere`}>
        <h2 id={`${id}-everywhere`} className="account__visually-hidden">
          {t("signOutEverywhere")}
        </h2>
        <button
          type="button"
          className="auth__button account__button--danger"
          onClick={() =>
            void client.signOutEverywhere().then(
              () => setSignedOutElsewhere(true),
              () => undefined,
            )
          }
        >
          {t("signOutEverywhere")}
        </button>
        <span className="auth__hint">{t("signOutEverywhereHint")}</span>
        {signedOutElsewhere && (
          <span className="account__ok" role="status">
            {t("signedOutEverywhere")}
          </span>
        )}
      </section>

      {adding !== null && (
        <Dialog id={`${id}-add`} title={t("addPasskey")} onClose={() => setAdding(null)}>
          <form className="auth__stack" onSubmit={(event) => void saveAdded(event)}>
            <div className="auth__field">
              <label className="auth__label" htmlFor={`${id}-add-name`}>
                {t("passkeyName")}
              </label>
              <input id={`${id}-add-name`} className="auth__input" autoFocus maxLength={100} value={adding.name} onChange={(event) => setAdding({ ...adding, name: event.target.value })} />
            </div>
            <div className="account__dialog-actions">
              <button type="button" className="auth__button" onClick={() => setAdding(null)}>
                {t("cancel")}
              </button>
              <button type="submit" className="auth__button auth__button--primary">
                {t("save")}
              </button>
            </div>
          </form>
        </Dialog>
      )}

      {confirm !== null && (
        <ConfirmDialog
          id={`${id}-confirm`}
          locale={locale}
          lead={t(confirmText[confirm.kind][0], confirm.kind === "remove" ? { name: confirm.passkey.name ?? t("unnamedPasskey") } : {})}
          action={t(confirmText[confirm.kind][1])}
          onConfirm={confirmed}
          onCancel={() => setConfirm(null)}
        />
      )}
    </main>
  );
}

function Section({ id, number, title, aside, children }: { id: string; number: number; title: string; aside?: string; children: ReactNode }) {
  return (
    <section className="account__card" aria-labelledby={id}>
      <header className="account__card-head">
        <span className="account__number" aria-hidden="true">
          {number}
        </span>
        <h2 id={id}>{title}</h2>
        {aside !== undefined && <span className="account__aside">{aside}</span>}
      </header>
      {children}
    </section>
  );
}

function Dialog({ title, onClose, children }: { id: string; title: string; onClose: () => void; children: ReactNode }) {
  return (
    <SharedDialog title={title} onClose={onClose} className="account__dialog">
      {children}
    </SharedDialog>
  );
}

/** Asks for the password before a sensitive change (W4 D); a wrong password is shown at the field. */
function ConfirmDialog({ id, locale, lead, action, onConfirm, onCancel }: { id: string; locale: Locale; lead: string; action: string; onConfirm: (password: string) => Promise<void>; onCancel: () => void }) {
  const t = createTranslator(accountMessages, locale);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<AccountMessageKey | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);

    try {
      await onConfirm(password);
    } catch (caught) {
      setError(caught instanceof ApiError && caught.problem.errors?.password !== undefined ? "passwordWrong" : "somethingWrong");
      setBusy(false);
    }
  }

  return (
    <Dialog id={`${id}-title`} title={t("confirmTitle")} onClose={onCancel}>
      <form className="auth__stack" onSubmit={(event) => void submit(event)} noValidate>
        <p className="auth__lead">{lead}</p>
        <div className="auth__field">
          <label className="auth__label" htmlFor={`${id}-password`}>
            {t("password")}
          </label>
          <input
            id={`${id}-password`}
            className="auth__input"
            type="password"
            autoComplete="current-password"
            autoFocus
            value={password}
            aria-invalid={error === "passwordWrong"}
            aria-describedby={error !== null ? `${id}-error` : undefined}
            onChange={(event) => setPassword(event.target.value)}
          />
          {error !== null && (
            <span id={`${id}-error`} className="auth__error">
              {t(error)}
            </span>
          )}
        </div>
        <div className="account__dialog-actions">
          <button type="button" className="auth__button" onClick={onCancel}>
            {t("cancel")}
          </button>
          <button type="submit" className="auth__button auth__button--primary" disabled={busy}>
            {action}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

const strengthLevels = ["strengthWeak", "strengthFair", "strengthGood", "strengthStrong"] as const;

/** A rough guide only; the server decides (at least 12 characters). */
function strength(password: string): number {
  if (password.length < 12) {
    return 0;
  }

  return Math.min(3, 1 + Number(password.length >= 16) + Number(/[^A-Za-z]/.test(password)) + Number(/[a-z]/.test(password) && /[A-Z]/.test(password)));
}

type PasswordErrors = { current?: AccountMessageKey; next?: AccountMessageKey; repeat?: AccountMessageKey };

function PasswordSection({ client, locale }: { client: AuthClient; locale: Locale }) {
  const t = createTranslator(accountMessages, locale);
  const id = useId();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [repeat, setRepeat] = useState("");
  const [errors, setErrors] = useState<PasswordErrors>({});
  const [changed, setChanged] = useState(false);
  const [busy, setBusy] = useState(false);
  const level = strength(next);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setChanged(false);

    if (next !== repeat) {
      setErrors({ repeat: "newPasswordsDiffer" });
      return;
    }

    setBusy(true);

    try {
      await client.changePassword(current, next);
      setErrors({});
      setCurrent("");
      setNext("");
      setRepeat("");
      setChanged(true);
    } catch (caught) {
      const fields = caught instanceof ApiError ? caught.problem.errors : undefined;

      if (fields?.currentPassword !== undefined) {
        setErrors({ current: "passwordWrong" });
      } else if (fields?.newPassword?.includes("password-too-short")) {
        setErrors({ next: "passwordTooShort" });
      } else {
        setErrors({ next: "somethingWrong" });
      }
    } finally {
      setBusy(false);
    }
  }

  const field = (key: keyof PasswordErrors, label: AccountMessageKey, value: string, onChange: (value: string) => void, autoComplete: string, hint?: ReactNode) => (
    <div className="auth__field">
      <label className="auth__label" htmlFor={`${id}-${key}`}>
        {t(label)}
      </label>
      <input
        id={`${id}-${key}`}
        className="auth__input"
        type="password"
        autoComplete={autoComplete}
        value={value}
        aria-invalid={errors[key] !== undefined}
        aria-describedby={errors[key] !== undefined ? `${id}-${key}-error` : undefined}
        onChange={(event) => onChange(event.target.value)}
      />
      {errors[key] !== undefined && (
        <span id={`${id}-${key}-error`} className="auth__error">
          {t(errors[key])}
        </span>
      )}
      {hint}
    </div>
  );

  return (
    <Section id={`${id}-title`} number={3} title={t("password")}>
      <form className="auth__stack" onSubmit={(event) => void submit(event)} noValidate>
        {field("current", "currentPassword", current, setCurrent, "current-password")}
        <div className="account__grid">
          {field(
            "next",
            "newPassword",
            next,
            setNext,
            "new-password",
            <span className="auth__hint">
              {t("newPasswordHint")}
              {next !== "" && (
                <>
                  {" "}
                  {t("strength", { level: t(strengthLevels[level]!) })}{" "}
                  <span className="account__meter" aria-hidden="true">
                    {[0, 1, 2, 3].map((bar) => (
                      <i key={bar} className={bar <= level ? "account__meter-on" : undefined} />
                    ))}
                  </span>
                </>
              )}
            </span>,
          )}
          {field("repeat", "repeatNewPassword", repeat, setRepeat, "new-password")}
        </div>
        <div className="account__actions">
          <button type="submit" className="auth__button auth__button--primary" disabled={busy}>
            {t("changePassword")}
          </button>
          <span className="auth__hint">{t("changePasswordHint")}</span>
        </div>
        {changed && (
          <span className="account__ok" role="status">
            {t("passwordChangedElsewhere")}
          </span>
        )}
      </form>
    </Section>
  );
}
