// SPDX-License-Identifier: AGPL-3.0-only
import "../designer/designer.css";
import "./members.css";
import { ApiError, type DirectoryTest, type MappedRole, type SignInSettings, type SignInSettingsChange, type SignInSettingsClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, Field, Notice, RadioGroup, Switch } from "@askwerk/ui";
import { useEffect, useId, useMemo, useState } from "react";
import { signInMessages, type SignInMessageKey } from "./signInMessages";

type T = (key: SignInMessageKey, values?: Record<string, string | number>) => string;
type Draft = SignInSettingsChange;

const mapped: readonly MappedRole[] = ["admin", "editor", "analyst", "viewer"];
const notLetIn = "none";

/**
 * Organisation → Sign-in (AWK-12, W22): owners connect their product's sign-in. Off by default, so the page shows
 * Askwerk accounts and two switches; each switch unfolds only what it needs.
 */
export function SignInPage({ client, locale, writeClipboard = (text) => navigator.clipboard.writeText(text), origin = location.origin }: { client: SignInSettingsClient; locale: Locale; writeClipboard?: (text: string) => Promise<void>; origin?: string }) {
  const t: T = useMemo(() => createTranslator(signInMessages, locale), [locale]);
  const [saved, setSaved] = useState<SignInSettings | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [state, setState] = useState<"loading" | "loaded" | "refused" | "failed">("loading");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<"saved" | "failed" | null>(null);
  const id = useId();

  useEffect(() => {
    client.get().then(
      (settings) => {
        setSaved(settings);
        setDraft({ ...settings, oidcClientSecret: null, ldapBindPassword: null });
        setState("loaded");
      },
      (failure: unknown) => setState(failure instanceof ApiError && failure.status === 403 ? "refused" : "failed"),
    );
  }, [client]);

  if (state === "refused" || state === "failed") {
    return (
      <main className="members" lang={locale}>
        <h1>{t("title")}</h1>
        <p role="alert">{t(state === "refused" ? "notAllowed" : "failed")}</p>
      </main>
    );
  }

  if (draft === null || saved === null) {
    return <main className="members" lang={locale} aria-busy="true" />;
  }

  const change = (patch: Partial<Draft>) => setDraft({ ...draft, ...patch });
  const keyAt = (index: number) => draft.keys[index] ?? "";
  const setKey = (index: number, value: string) => change({ keys: [keyAt(0), keyAt(1)].map((key, at) => (at === index ? value : key)).filter((key, at) => key !== "" || at === 0) });
  const errorOf = (field: string) => (errors[field] === undefined ? undefined : t(errors[field] as SignInMessageKey));

  const save = () => {
    setErrors({});
    client.save({ ...draft, keys: draft.keys.filter((key) => key.trim() !== "") }).then(
      (settings) => {
        setSaved(settings);
        setDraft({ ...settings, oidcClientSecret: null, ldapBindPassword: null });
        setNotice("saved");
      },
      (failure: unknown) => {
        const fields = failure instanceof ApiError ? failure.problem.errors : undefined;

        if (fields !== undefined) {
          setErrors(Object.fromEntries(Object.entries(fields).map(([field, codes]) => [field.charAt(0).toLowerCase() + field.slice(1), codes[0] ?? "failed"])));
        } else {
          setNotice("failed");
        }
      },
    );
  };

  const roleOptions = mapped.map((role) => ({ value: role, label: t(role) }));

  return (
    <main className="members sign-in-settings" lang={locale}>
      <h1>{t("title")}</h1>
      <p className="members__lead">{t("intro")}</p>

      <section className="sign-in-settings__section">
        <Switch label={<span>{t("accounts")}</span>} checked disabled onChange={() => undefined} />
        <p className="members__muted">{t("accountsHint")}</p>
      </section>

      {!draft.handoverEnabled && !draft.provisioningEnabled && !draft.oidcEnabled && !draft.ldapEnabled && (
        <p className="sign-in-settings__connect">
          <b>{t("connect")}</b> <span className="members__muted">{t("connectHint")}</span>
        </p>
      )}

      <section className="sign-in-settings__section">
        <Switch label={<span>{t("handover")}</span>} checked={draft.handoverEnabled} onChange={(on) => change({ handoverEnabled: on })} />
        <p className="members__muted">{t("handoverHint")}</p>
        {draft.handoverEnabled && (
          <div className="sign-in-settings__fields">
            <Field label={t("issuer")} error={errorOf("issuer")}>
              <input className="designer-input" value={draft.issuer ?? ""} onChange={(event) => change({ issuer: event.target.value })} placeholder="https://hr.example.org" />
            </Field>
            <Field label={t("keys")} hint={t("keysHint")} error={errorOf("keys")}>
              <textarea className="designer-input sign-in-settings__key" rows={4} value={keyAt(0)} onChange={(event) => setKey(0, event.target.value)} placeholder="-----BEGIN PUBLIC KEY-----" />
            </Field>
            {keyAt(0) !== "" && (
              <Field label={t("secondKey")}>
                <textarea className="designer-input sign-in-settings__key" rows={3} value={keyAt(1)} onChange={(event) => setKey(1, event.target.value)} />
              </Field>
            )}
            <Field label={t("jwksUrl")} hint={t("jwksHint")} error={errorOf("jwksUrl")}>
              <input className="designer-input" value={draft.jwksUrl ?? ""} onChange={(event) => change({ jwksUrl: event.target.value })} placeholder="https://hr.example.org/.well-known/jwks.json" />
            </Field>
            <Copyable label={t("audience")} value={saved.audience} t={t} writeClipboard={writeClipboard} />
          </div>
        )}
      </section>

      <section className="sign-in-settings__section">
        <Switch label={<span>{t("oidc")}</span>} checked={draft.oidcEnabled} onChange={(on) => change({ oidcEnabled: on })} />
        <p className="members__muted">{t("oidcHint")}</p>
        {draft.oidcEnabled && (
          <div className="sign-in-settings__fields">
            <Field label={t("oidcAuthority")} hint={t("oidcAuthorityHint")} error={errorOf("oidcAuthority")}>
              <input className="designer-input" value={draft.oidcAuthority ?? ""} onChange={(event) => change({ oidcAuthority: event.target.value })} placeholder="https://login.microsoftonline.com/…/v2.0" />
            </Field>
            <Field label={t("oidcClientId")} error={errorOf("oidcClientId")}>
              <input className="designer-input" value={draft.oidcClientId ?? ""} onChange={(event) => change({ oidcClientId: event.target.value })} />
            </Field>
            <Field label={t("oidcClientSecret")} hint={saved.oidcClientSecretSet ? t("oidcSecretSet") : t("oidcSecretHint")}>
              <input className="designer-input" type="password" autoComplete="off" value={draft.oidcClientSecret ?? ""} onChange={(event) => change({ oidcClientSecret: event.target.value === "" ? null : event.target.value })} />
            </Field>
            <Field label={t("oidcDisplayName")} hint={t("oidcDisplayNameHint")}>
              <input className="designer-input" value={draft.oidcDisplayName ?? ""} onChange={(event) => change({ oidcDisplayName: event.target.value })} placeholder="Acme" maxLength={100} />
            </Field>
            <Copyable label={t("oidcRedirect")} value={origin + saved.oidcCallbackPath} t={t} writeClipboard={writeClipboard} />
            <Copyable label={t("ssoLink")} value={origin + saved.ssoPath} t={t} writeClipboard={writeClipboard} />
          </div>
        )}
      </section>

      <section className="sign-in-settings__section">
        <Switch label={<span>{t("ldap")}</span>} checked={draft.ldapEnabled} onChange={(on) => change({ ldapEnabled: on })} />
        <p className="members__muted">{t("ldapHint")}</p>
        {draft.ldapEnabled && (
          <div className="sign-in-settings__fields">
            <RadioGroup
              legend={t("ldapKind")}
              value={draft.ldapLoginAttribute === "sAMAccountName" ? "ad" : "ldap"}
              options={[
                { value: "ad", label: t("ldapActiveDirectory") },
                { value: "ldap", label: t("ldapOther") },
              ]}
              onChange={(kind) =>
                change(kind === "ad" ? { ldapLoginAttribute: "sAMAccountName", ldapObjectClass: "user", ldapGroupAttribute: "memberOf" } : { ldapLoginAttribute: "uid", ldapObjectClass: "person", ldapGroupAttribute: "memberOf" })
              }
            />
            <Field label={t("ldapHost")} hint={t("ldapHostHint")} error={errorOf("ldapHost")}>
              <input className="designer-input" value={draft.ldapHost ?? ""} onChange={(event) => change({ ldapHost: event.target.value })} placeholder="dc1.acme.local" />
            </Field>
            <Field label={t("ldapBindDn")} error={errorOf("ldapBindDn")}>
              <input className="designer-input" value={draft.ldapBindDn ?? ""} onChange={(event) => change({ ldapBindDn: event.target.value })} placeholder="cn=askwerk,ou=service,dc=acme,dc=local" />
            </Field>
            <Field label={t("ldapBindPassword")} hint={saved.ldapBindPasswordSet ? t("oidcSecretSet") : undefined}>
              <input className="designer-input" type="password" autoComplete="off" value={draft.ldapBindPassword ?? ""} onChange={(event) => change({ ldapBindPassword: event.target.value === "" ? null : event.target.value })} />
            </Field>
            <Field label={t("ldapBaseDn")} error={errorOf("ldapBaseDn")}>
              <input className="designer-input" value={draft.ldapBaseDn ?? ""} onChange={(event) => change({ ldapBaseDn: event.target.value })} placeholder="ou=people,dc=acme,dc=local" />
            </Field>
            <details className="sign-in-settings__more">
              <summary>{t("ldapMore")}</summary>
              <div className="sign-in-settings__fields">
                <Field label={t("ldapPort")} error={errorOf("ldapPort")}>
                  <input className="designer-input" inputMode="numeric" value={String(draft.ldapPort)} onChange={(event) => change({ ldapPort: Number(event.target.value) || 0 })} />
                </Field>
                <Field label={t("ldapLoginAttribute")} error={errorOf("ldapLoginAttribute")}>
                  <input className="designer-input" value={draft.ldapLoginAttribute} onChange={(event) => change({ ldapLoginAttribute: event.target.value })} />
                </Field>
                <Field label={t("ldapObjectClass")}>
                  <input className="designer-input" value={draft.ldapObjectClass} onChange={(event) => change({ ldapObjectClass: event.target.value })} />
                </Field>
                <Field label={t("ldapGroupAttribute")}>
                  <input className="designer-input" value={draft.ldapGroupAttribute} onChange={(event) => change({ ldapGroupAttribute: event.target.value })} />
                </Field>
                <Field label={t("ldapCa")} hint={t("ldapCaHint")} error={errorOf("ldapCaCertificate")}>
                  <textarea className="designer-input sign-in-settings__key" rows={3} value={draft.ldapCaCertificate ?? ""} onChange={(event) => change({ ldapCaCertificate: event.target.value })} placeholder="-----BEGIN CERTIFICATE-----" />
                </Field>
                <Field label={t("oidcDisplayName")} hint={t("oidcDisplayNameHint")}>
                  <input className="designer-input" value={draft.ldapDisplayName ?? ""} onChange={(event) => change({ ldapDisplayName: event.target.value })} maxLength={100} />
                </Field>
              </div>
            </details>
            {saved.ldapEnabled && <DirectoryCheck t={t} client={client} />}
          </div>
        )}
      </section>

      <section className="sign-in-settings__section">
        <Switch label={<span>{t("provisioning")}</span>} checked={draft.provisioningEnabled} onChange={(on) => change({ provisioningEnabled: on })} />
        <p className="members__muted">{t("provisioningHint")}</p>
        {draft.provisioningEnabled && (
          <div className="sign-in-settings__fields">
            <Field label={t("systemName")} hint={t("systemNameHint")}>
              <input className="designer-input" value={draft.systemName ?? ""} onChange={(event) => change({ systemName: event.target.value })} placeholder="HR Suite" maxLength={100} />
            </Field>
          </div>
        )}
      </section>

      {(draft.handoverEnabled || draft.oidcEnabled || draft.ldapEnabled) && (
        <section className="sign-in-settings__section">
          <h2>{t("roles")}</h2>
          <p className="members__muted">{t("rolesHint")}</p>
          {errorOf("roleRules") !== undefined && <p role="alert" className="members__error">{errorOf("roleRules")}</p>}
          <ol className="sign-in-settings__rules">
            {draft.roleRules.map((rule, index) => (
              <li key={index}>
                <input
                  className="designer-input"
                  aria-label={t("groupOf", { number: index + 1 })}
                  value={rule.group}
                  placeholder={t("group")}
                  onChange={(event) => change({ roleRules: draft.roleRules.map((each, at) => (at === index ? { ...each, group: event.target.value } : each)) })}
                />
                <span aria-hidden="true">→</span>
                <span id={`${id}-rule-${index}`} className="aw-visually-hidden">
                  {t("roleOf", { number: index + 1 })}
                </span>
                <ComboBox
                  labelledBy={`${id}-rule-${index}`}
                  options={roleOptions}
                  value={rule.role}
                  onChange={(value) => change({ roleRules: draft.roleRules.map((each, at) => (at === index ? { ...each, role: value as MappedRole } : each)) })}
                  texts={{ choose: t("roleOf", { number: index + 1 }) }}
                />
                <button type="button" className="members__link" aria-label={t("removeRule", { number: index + 1 })} onClick={() => change({ roleRules: draft.roleRules.filter((_, at) => at !== index) })}>
                  ✕
                </button>
              </li>
            ))}
          </ol>
          <button type="button" className="designer-button designer-button--quiet" onClick={() => change({ roleRules: [...draft.roleRules, { group: "", role: "viewer" }] })}>
            {t("addRule")}
          </button>
          <div className="sign-in-settings__default">
            <span id={`${id}-default`} className="aw-label">
              {t("anyoneElse")}
            </span>
            <ComboBox
              labelledBy={`${id}-default`}
              options={[...roleOptions, { value: notLetIn, label: t("notLetIn") }]}
              value={draft.defaultRole ?? notLetIn}
              onChange={(value) => change({ defaultRole: value === notLetIn ? null : (value as MappedRole) })}
              texts={{ choose: t("anyoneElse") }}
            />
          </div>
        </section>
      )}

      <div className="sign-in-settings__actions">
        <button type="button" className="designer-button designer-button--primary" onClick={save}>
          {t("save")}
        </button>
      </div>
      {notice !== null && (
        <Notice kind={notice === "saved" ? "done" : "error"} onClose={() => setNotice(null)} closeLabel={t("close")}>
          {t(notice)}
        </Notice>
      )}
    </main>
  );
}

/** A value to give the host or the provider, with a copy button. */
function Copyable({ label, value, t, writeClipboard }: { label: string; value: string; t: T; writeClipboard: (text: string) => Promise<void> }) {
  const [copied, setCopied] = useState(false);

  return (
    <div className="sign-in-settings__audience">
      <span className="aw-label">{label}</span>
      <code>{value}</code>
      <button
        type="button"
        className="designer-button designer-button--quiet"
        onClick={() =>
          void writeClipboard(value).then(
            () => setCopied(true),
            () => undefined,
          )
        }
      >
        {copied ? t("copied") : t("copy")}
      </button>
    </div>
  );
}

/** "Test connection" with a test person: what the saved settings find, and the role they would give. */
function DirectoryCheck({ t, client }: { t: T; client: SignInSettingsClient }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [result, setResult] = useState<DirectoryTest | null>(null);

  return (
    <div className="sign-in-settings__check">
      <span className="aw-label">{t("ldapTest")}</span>
      <div className="sign-in-settings__check-row">
        <input className="designer-input" aria-label={t("ldapTestUser")} placeholder={t("ldapTestUser")} value={username} onChange={(event) => setUsername(event.target.value)} />
        <input className="designer-input" type="password" autoComplete="off" aria-label={t("ldapTestPassword")} placeholder={t("ldapTestPassword")} value={password} onChange={(event) => setPassword(event.target.value)} />
        <button type="button" className="designer-button designer-button--quiet" onClick={() => void client.testDirectory(username, password).then(setResult, () => setResult({ ok: false, problem: "directory-unreachable", email: null, name: null, groups: [], role: null }))}>
          {t("ldapTestRun")}
        </button>
      </div>
      {result !== null && (
        <p role="status">
          {result.ok
            ? t("ldapTestOk", { name: result.name ?? "", email: result.email ?? "", role: result.role === null ? t("notLetIn") : t(result.role) })
            : t((result.problem ?? "directory-unreachable") as SignInMessageKey)}
        </p>
      )}
    </div>
  );
}
