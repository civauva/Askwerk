// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, connectionGoals, connectionPermissions, type ConnectionCredentials, type ConnectionGoal, type ConnectionItem, type ConnectionPermission, type ConnectionsClient } from "@askwerk/app-client";
import type { Locale } from "@askwerk/schema";
import { Dialog } from "@askwerk/ui";
import { useEffect, useId, useRef, useState } from "react";
import { ago, CodeTabs, Credentials, eventKeys, goalKeys, permissionKeys, setupOf, shortUrl, type T } from "./parts";
import type { ConnectionsMessageKey } from "./messages";

type ConnectWizardProps = {
  t: T;
  client: ConnectionsClient;
  /** The organisation's surveys, for "Only these". */
  surveys: readonly { id: string; title: string }[];
  locale: Locale;
  baseUrl: string;
  writeClipboard: (text: string) => Promise<void>;
  onNavigate: (path: string) => void;
  onClose: (changed: boolean) => void;
  /** How often step 3 looks for the first call and the test delivery. */
  pollMs?: number;
};

type Errors = Partial<Record<"name" | "webhookUrl" | "permissions" | "form", ConnectionsMessageKey>>;

/** The permissions a set of goals needs. */
function presetOf(goals: readonly ConnectionGoal[], webhookUrl: string): ConnectionPermission[] {
  return [...(goals.includes("people") ? (["people"] as const) : []), ...(goals.includes("answers") && webhookUrl.trim() === "" ? (["results"] as const) : [])];
}

/**
 * Connect a system (W36, AWK-49) in three steps: what it should do; the details, preset from that; then a test in
 * both directions, the secrets once and the code to run.
 */
export function ConnectWizard({ t, client, surveys, locale, baseUrl, writeClipboard, onNavigate, onClose, pollMs = 3000 }: ConnectWizardProps) {
  const id = useId();
  const [step, setStep] = useState<0 | 1 | 2>(0);
  const [goals, setGoals] = useState<ConnectionGoal[]>([]);
  const [name, setName] = useState("");
  const [webhookUrl, setWebhookUrl] = useState("");
  const [events, setEvents] = useState<string[]>(["response.completed"]);
  const [onlySome, setOnlySome] = useState(false);
  const [surveyIds, setSurveyIds] = useState<string[]>([]);
  const [signIn, setSignIn] = useState<"oauth" | "api-key">("oauth");
  const [permissions, setPermissions] = useState<ConnectionPermission[]>([]);
  const [touchedPermissions, setTouchedPermissions] = useState(false);
  const [matchBy, setMatchBy] = useState<"external-id" | "email">("external-id");
  const [errors, setErrors] = useState<Errors>({});
  const [busy, setBusy] = useState(false);
  const [created, setCreated] = useState<{ connection: ConnectionItem; credentials: ConnectionCredentials } | null>(null);

  const answers = goals.includes("answers");
  const people = goals.includes("people");
  const calls = people || (answers && webhookUrl.trim() === "");
  const shownPermissions = touchedPermissions ? permissions : presetOf(goals, webhookUrl);
  const steps: ConnectionsMessageKey[] = ["stepGoals", "stepDetails", "stepTest"];

  const next = () => {
    setSignIn(people ? "oauth" : "api-key");
    setStep(1);
  };

  const create = () => {
    const found: Errors = {};
    name.trim() === "" && (found.name = "nameRequired");
    calls && shownPermissions.length === 0 && (found.permissions = "permissionsRequired");
    setErrors(found);

    if (Object.keys(found).length > 0) {
      return;
    }

    setBusy(true);
    client
      .create({
        name: name.trim(),
        goals,
        ...(answers && webhookUrl.trim() !== "" ? { webhookUrl: webhookUrl.trim(), events, surveyIds: onlySome ? surveyIds : [] } : {}),
        ...(calls ? { signIn, permissions: shownPermissions } : {}),
        ...(people ? { matchBy } : {}),
      })
      .then(
        (answer) => {
          setCreated(answer);
          setStep(2);
        },
        (caught: unknown) => {
          const problem = caught instanceof ApiError ? caught.problem.errors : undefined;
          setErrors(problem?.webhookUrl !== undefined ? { webhookUrl: "urlInvalid" } : problem?.name !== undefined ? { name: "nameRequired" } : { form: "failed" });
        },
      )
      .finally(() => setBusy(false));
  };

  return (
    <Dialog title={t("wizardTitle")} onClose={() => onClose(created !== null)} wide className="connect-wizard">
      <ol className="connect-steps" aria-label={t("steps")}>
        {steps.map((key, index) => (
          <li key={key} aria-current={index === step ? "step" : undefined} data-done={index < step ? "" : undefined}>
            <span aria-hidden="true">{index < step ? "✓" : index + 1}</span>
            {t(key)}
          </li>
        ))}
      </ol>

      {step === 0 && (
        <>
          <p className="connection-muted">{t("chooseGoals")}</p>
          <fieldset className="connect-goals">
            <legend className="aw-visually-hidden">{t("goals")}</legend>
            {connectionGoals.map((goal) => (
              <label key={goal} className="connect-goal" data-chosen={goals.includes(goal) ? "" : undefined}>
                <input type="checkbox" checked={goals.includes(goal)} onChange={(event) => setGoals((current) => (event.target.checked ? connectionGoals.filter((each) => each === goal || current.includes(each)) : current.filter((each) => each !== goal)))} />
                <span className="connect-goal__mark" aria-hidden="true">
                  {goalKeys[goal].mark}
                </span>
                <span>
                  <b>{t(goalKeys[goal].title)}</b>
                  <span>{t(goalKeys[goal].text)}</span>
                </span>
              </label>
            ))}
          </fieldset>
          <div className="connect-actions">
            <button type="button" className="designer-button" onClick={() => onClose(false)}>
              {t("cancel")}
            </button>
            <button type="button" className="designer-button designer-button--primary" disabled={goals.length === 0} onClick={next}>
              {t("next")}
            </button>
          </div>
        </>
      )}

      {step === 1 && (
        <>
          <div className="connect-form">
            <label htmlFor={`${id}-name`}>
              {t("name")}
              <small>{t("nameHint")}</small>
            </label>
            <span>
              <input id={`${id}-name`} className="designer-input" value={name} aria-invalid={errors.name !== undefined} onChange={(event) => setName(event.target.value)} />
              {errors.name !== undefined && <span className="designer-error">{t(errors.name)}</span>}
            </span>

            {answers && (
              <>
                <label htmlFor={`${id}-url`}>
                  {t("answersTo")}
                  <small>{t("answersToHint")}</small>
                </label>
                <span>
                  <input id={`${id}-url`} className="designer-input connection-mono" type="url" placeholder="https://" value={webhookUrl} aria-invalid={errors.webhookUrl !== undefined} onChange={(event) => setWebhookUrl(event.target.value)} />
                  {errors.webhookUrl !== undefined && <span className="designer-error">{t(errors.webhookUrl)}</span>}
                </span>
                {webhookUrl.trim() !== "" && (
                  <>
                    <span id={`${id}-when`}>{t("when")}</span>
                    <span role="group" aria-labelledby={`${id}-when`} className="connect-checks">
                      {eventKeys.map(([event, key]) => (
                        <label key={event}>
                          <input type="checkbox" checked={events.includes(event)} onChange={(changed) => setEvents((current) => (changed.target.checked ? [...current, event] : current.filter((each) => each !== event)))} />
                          {t(key)} <code>{event}</code>
                        </label>
                      ))}
                    </span>
                    <span id={`${id}-surveys`}>{t("whichSurveys")}</span>
                    <span role="radiogroup" aria-labelledby={`${id}-surveys`} className="connect-checks">
                      <label>
                        <input type="radio" name={`${id}-scope`} checked={!onlySome} onChange={() => setOnlySome(false)} />
                        {t("allSurveys")}
                      </label>
                      <label>
                        <input type="radio" name={`${id}-scope`} checked={onlySome} onChange={() => setOnlySome(true)} />
                        {t("onlyThese")}
                      </label>
                      {onlySome && (
                        <span className="connect-surveys">
                          {surveys.map((survey) => (
                            <label key={survey.id}>
                              <input type="checkbox" checked={surveyIds.includes(survey.id)} onChange={(changed) => setSurveyIds((current) => (changed.target.checked ? [...current, survey.id] : current.filter((each) => each !== survey.id)))} />
                              {survey.title}
                            </label>
                          ))}
                        </span>
                      )}
                    </span>
                  </>
                )}
              </>
            )}

            {calls && (
              <>
                <span id={`${id}-sign`}>
                  {t("howSignsIn")}
                  <small>{t("signInHint")}</small>
                </span>
                <span role="radiogroup" aria-labelledby={`${id}-sign`} className="connect-checks">
                  <label>
                    <input type="radio" name={`${id}-sign`} checked={signIn === "oauth"} onChange={() => setSignIn("oauth")} />
                    {t("oauthOption")}
                  </label>
                  <label>
                    <input type="radio" name={`${id}-sign`} checked={signIn === "api-key"} onChange={() => setSignIn("api-key")} />
                    {t("keyOption")}
                  </label>
                </span>
                <span id={`${id}-may`}>
                  {t("itMay")}
                  <small>{t("permHint")}</small>
                </span>
                <span role="group" aria-labelledby={`${id}-may`} className="connect-checks">
                  {connectionPermissions.map((permission) => (
                    <label key={permission}>
                      <input
                        type="checkbox"
                        checked={shownPermissions.includes(permission)}
                        onChange={(changed) => {
                          setTouchedPermissions(true);
                          setPermissions(changed.target.checked ? connectionPermissions.filter((each) => each === permission || shownPermissions.includes(each)) : shownPermissions.filter((each) => each !== permission));
                        }}
                      />
                      {t(permissionKeys[permission].long)} <code>{permissionKeys[permission].scopes}</code>
                    </label>
                  ))}
                  {errors.permissions !== undefined && <span className="designer-error">{t(errors.permissions)}</span>}
                </span>
              </>
            )}

            {people && (
              <>
                <span id={`${id}-match`}>{t("matchBy")}</span>
                <span role="radiogroup" aria-labelledby={`${id}-match`} className="connect-checks">
                  <label>
                    <input type="radio" name={`${id}-match`} checked={matchBy === "external-id"} onChange={() => setMatchBy("external-id")} />
                    <span>
                      {t("matchExternal")}
                      <small>{t("matchExternalHint")}</small>
                    </span>
                  </label>
                  <label>
                    <input type="radio" name={`${id}-match`} checked={matchBy === "email"} onChange={() => setMatchBy("email")} />
                    {t("matchEmail")}
                  </label>
                </span>
              </>
            )}
          </div>

          {goals.includes("sign-in") && (
            <p className="connect-note">
              {t("signInNote")}{" "}
              <button type="button" className="designer-link-button" onClick={() => onNavigate("/sign-in-settings")}>
                {t("openSignIn")}
              </button>
            </p>
          )}
          {goals.includes("embed") && (
            <p className="connect-note">
              {t("embedNote")}{" "}
              <button type="button" className="designer-link-button" onClick={() => onNavigate("/domains")}>
                {t("openDomains")}
              </button>
            </p>
          )}
          {errors.form !== undefined && (
            <p role="alert" className="designer-error">
              {t(errors.form)}
            </p>
          )}
          <div className="connect-actions">
            <button type="button" className="designer-button" onClick={() => setStep(0)}>
              {t("back")}
            </button>
            <button type="button" className="designer-button designer-button--primary" disabled={busy} onClick={create}>
              {t("create")}
            </button>
          </div>
        </>
      )}

      {step === 2 && created !== null && <TestStep t={t} client={client} created={created} locale={locale} baseUrl={baseUrl} writeClipboard={writeClipboard} pollMs={pollMs} onDone={() => onClose(true)} />}
    </Dialog>
  );
}

/** Step 3: a test event to the system, the first call from it, the secrets once and the code. */
function TestStep({ t, client, created, locale, baseUrl, writeClipboard, pollMs, onDone }: { t: T; client: ConnectionsClient; created: { connection: ConnectionItem; credentials: ConnectionCredentials }; locale: Locale; baseUrl: string; writeClipboard: (text: string) => Promise<void>; pollMs: number; onDone: () => void }) {
  const [connection, setConnection] = useState(created.connection);
  const [test, setTest] = useState<{ sentAt: number; polls: number } | null>(null);
  const testRef = useRef(test);
  testRef.current = test;
  const { webhook, calls } = connection;
  const host = webhook === null ? "" : shortUrl(webhook.url).split("/")[0] ?? "";
  const delivered = test !== null && webhook?.lastDeliveredAt != null && Date.parse(webhook.lastDeliveredAt) >= test.sentAt - 1000;

  useEffect(() => {
    const timer = setInterval(() => {
      void client.get(connection.id).then(
        (fresh) => {
          setConnection(fresh);
          testRef.current !== null && setTest((current) => (current === null ? null : { ...current, polls: current.polls + 1 }));
        },
        () => undefined,
      );
    }, pollMs);

    return () => clearInterval(timer);
  }, [client, connection.id, pollMs]);

  const sendTest = () => client.test(connection.id).then(() => setTest({ sentAt: Date.now(), polls: 0 }), () => undefined);

  return (
    <div className="connect-test">
      {webhook === null && calls === null && <p className="connection-muted">{t("nothingToTest")}</p>}
      {webhook !== null && (
        <section className="connect-direction" data-state={delivered ? "done" : undefined}>
          <h3>{t("toThem", { name: connection.name })}</h3>
          <p role="status">{test === null ? shortUrl(webhook.url) : delivered ? t("testDelivered", { host }) : test.polls > 6 ? t("testNotYet") : t("testWaiting", { host })}</p>
          <button type="button" className="designer-button" onClick={() => void sendTest()}>
            {test === null ? t("sendTest") : t("sendAnother")}
          </button>
        </section>
      )}
      {calls !== null && (
        <section className="connect-direction" data-state={calls.lastCallAt !== null ? "done" : undefined}>
          <h3>{t("toUs", { name: connection.name })}</h3>
          <p role="status">{calls.lastCallAt === null ? t("waitingCall") : t("calledAt", { when: ago(locale, calls.lastCallAt, new Date()) })}</p>
        </section>
      )}
      <Credentials t={t} credentials={created.credentials} writeClipboard={writeClipboard} />
      {(calls !== null || webhook !== null) && <CodeTabs t={t} setup={setupOf(connection, created.credentials.clientId, baseUrl)} />}
      <div className="connect-actions">
        <span />
        <button type="button" className="designer-button designer-button--primary" onClick={onDone}>
          {t("done")}
        </button>
      </div>
    </div>
  );
}
