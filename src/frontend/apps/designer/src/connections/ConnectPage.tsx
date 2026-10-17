// SPDX-License-Identifier: AGPL-3.0-only
import "../admin/admin.css";
import "./connections.css";
import { ApiError, connectionGoals, connectionPermissions, type ConnectionCredentials, type ConnectionGoal, type ConnectionItem, type ConnectionPermission, type ConnectionsClient, type SurveysClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useEffect, useId, useMemo, useRef, useState } from "react";
import { connectionsMessages, type ConnectionsMessageKey } from "./messages";
import { ago, CodeTabs, Credentials, eventOptions, goalKeys, guideOf, permissionKeys, setupOf, shortUrl, type T } from "./parts";

type ConnectPageProps = {
  client: ConnectionsClient;
  surveys: Pick<SurveysClient, "list">;
  locale: Locale;
  baseUrl?: string;
  writeClipboard: (text: string) => Promise<void>;
  onNavigate: (path: string) => void;
  /** How often step 3 looks for the first call and the test delivery. */
  pollMs?: number;
  now?: () => Date;
};

type Errors = Partial<Record<"name" | "webhookUrl" | "permissions" | "form", ConnectionsMessageKey>>;

/** The permissions a set of goals needs. */
function presetOf(goals: readonly ConnectionGoal[], webhookUrl: string): ConnectionPermission[] {
  return [...(goals.includes("people") ? (["people"] as const) : []), ...(goals.includes("answers") && webhookUrl.trim() === "" ? (["results"] as const) : [])];
}

/**
 * Connect a system (W36, AWK-49), a page in three steps: what it should do; only the details that goal needs, preset;
 * then both directions tested live, the secrets once, and code to copy with them filled in.
 */
export function ConnectPage({ client, surveys, locale, baseUrl = location.origin, writeClipboard, onNavigate, pollMs = 3000, now = () => new Date() }: ConnectPageProps) {
  const t = useMemo(() => createTranslator(connectionsMessages, locale), [locale]);
  const id = useId();
  const [step, setStep] = useState<0 | 1 | 2>(0);
  const [goals, setGoals] = useState<ConnectionGoal[]>([]);
  const [name, setName] = useState("");
  const [webhookUrl, setWebhookUrl] = useState("");
  const [events, setEvents] = useState<string[]>(["response.completed"]);
  const [onlySome, setOnlySome] = useState(false);
  const [picking, setPicking] = useState(false);
  const [surveyIds, setSurveyIds] = useState<string[]>([]);
  const [surveyList, setSurveyList] = useState<{ id: string; title: string }[]>([]);
  const [signIn, setSignIn] = useState<"oauth" | "api-key">("oauth");
  const [permissions, setPermissions] = useState<ConnectionPermission[]>([]);
  const [touchedPermissions, setTouchedPermissions] = useState(false);
  const [matchBy, setMatchBy] = useState<"external-id" | "email">("external-id");
  const [errors, setErrors] = useState<Errors>({});
  const [busy, setBusy] = useState(false);
  const [created, setCreated] = useState<{ connection: ConnectionItem; credentials: ConnectionCredentials } | null>(null);

  useEffect(() => {
    surveys.list({ limit: 100 }).then((list) => setSurveyList(list.items.map((survey) => ({ id: survey.id, title: survey.title }))), () => undefined);
  }, [surveys]);

  const answers = goals.includes("answers");
  const people = goals.includes("people");
  const calls = people || (answers && webhookUrl.trim() === "");
  const shownPermissions = touchedPermissions ? permissions : presetOf(goals, webhookUrl);
  const steps: ConnectionsMessageKey[] = ["stepGoals", "stepDetails", "stepTest"];

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
    <main className="admin admin--wide connect" lang={locale}>
      <h1 className="connections__title">{t("wizardTitle")}</h1>
      <ol className="connect-steps" aria-label={t("steps")}>
        {steps.map((key, index) => (
          <li key={key} aria-current={index === step ? "step" : undefined} data-done={index < step ? "" : undefined}>
            {index < step ? "✓" : index + 1} {t(key)}
          </li>
        ))}
      </ol>

      {step === 0 && (
        <>
          <p className="connections__lead">{t("chooseGoals")}</p>
          <fieldset className="connect-goals">
            <legend className="aw-visually-hidden">{t("goals")}</legend>
            {connectionGoals.map((goal) => (
              <label key={goal} className="connect-goal" data-chosen={goals.includes(goal) ? "" : undefined}>
                <input
                  type="checkbox"
                  className="connect-goal__input"
                  checked={goals.includes(goal)}
                  onChange={(event) => setGoals((current) => (event.target.checked ? connectionGoals.filter((each) => each === goal || current.includes(each)) : current.filter((each) => each !== goal)))}
                />
                <span className="connect-goal__check" aria-hidden="true" />
                <span className="connect-goal__icon" aria-hidden="true">
                  {goalKeys[goal].mark}
                </span>
                <b>{t(goalKeys[goal].title)}</b>
                <span className="connect-goal__text">{t(goalKeys[goal].text)}</span>
                <span className="connect-goal__uses">
                  {goalKeys[goal].uses.map((use) => (
                    <span key={use} className="connection-tag">
                      {use}
                    </span>
                  ))}
                </span>
              </label>
            ))}
          </fieldset>
          <div className="connect-actions">
            <button type="button" className="designer-button" onClick={() => onNavigate("/connections")}>
              {t("cancel")}
            </button>
            <button
              type="button"
              className="designer-button designer-button--primary"
              disabled={goals.length === 0}
              onClick={() => {
                setSignIn(people ? "oauth" : "api-key");
                setStep(1);
              }}
            >
              {t("next")}
            </button>
          </div>
        </>
      )}

      {step === 1 && (
        <section className="connect-card">
          <div className="connect-form">
            <label htmlFor={`${id}-name`} className="connect-form__label">
              {t("name")}
              <small>{t("nameHint")}</small>
            </label>
            <span>
              <input id={`${id}-name`} className="designer-input" value={name} aria-invalid={errors.name !== undefined} onChange={(event) => setName(event.target.value)} />
              {errors.name !== undefined && <span className="designer-error">{t(errors.name)}</span>}
            </span>

            {answers && (
              <>
                <label htmlFor={`${id}-url`} className="connect-form__label">
                  {t("answersTo")}
                  <small>{t("answersToHint")}</small>
                </label>
                <span className="connect-form__stack">
                  <input id={`${id}-url`} className="designer-input connection-mono" type="url" placeholder={t("answersToPlaceholder")} value={webhookUrl} aria-invalid={errors.webhookUrl !== undefined} onChange={(event) => setWebhookUrl(event.target.value)} />
                  {errors.webhookUrl !== undefined && <span className="designer-error">{t(errors.webhookUrl)}</span>}
                  {webhookUrl.trim() !== "" && (
                    <span role="group" aria-label={t("when")} className="connect-options">
                      {eventOptions.map((option) => (
                        <label key={option.label} className="connect-option">
                          <input
                            type="checkbox"
                            checked={option.events.every((event) => events.includes(event))}
                            onChange={(changed) => setEvents((current) => (changed.target.checked ? [...current, ...option.events.filter((event) => !current.includes(event))] : current.filter((event) => !option.events.includes(event))))}
                          />
                          <span>
                            {t(option.label)}
                            <small>{option.detail === null ? option.events.join(", ") : t(option.detail)}</small>
                          </span>
                        </label>
                      ))}
                    </span>
                  )}
                </span>
                {webhookUrl.trim() !== "" && (
                  <>
                    <span id={`${id}-surveys`} className="connect-form__label">
                      {t("whichSurveys")}
                    </span>
                    <span role="radiogroup" aria-labelledby={`${id}-surveys`} className="connect-options">
                      <label className="connect-option">
                        <input type="radio" name={`${id}-scope`} checked={!onlySome} onChange={() => setOnlySome(false)} />
                        <span>{t("allSurveys")}</span>
                      </label>
                      <label className="connect-option">
                        <input
                          type="radio"
                          name={`${id}-scope`}
                          checked={onlySome}
                          onChange={() => {
                            setOnlySome(true);
                            surveyIds.length === 0 && setPicking(true);
                          }}
                        />
                        <span className="connect-chosen">
                          {t("onlyThese")}
                          {surveyIds.length === 0 ? (
                            <span className="connection-muted">{t("noneChosen")}</span>
                          ) : (
                            surveyList
                              .filter((survey) => surveyIds.includes(survey.id))
                              .map((survey) => (
                                <span key={survey.id} className="connection-tag">
                                  {survey.title}
                                </span>
                              ))
                          )}
                          <button type="button" className="designer-link-button" aria-expanded={picking} onClick={() => setPicking(!picking)}>
                            {t("change")}
                          </button>
                        </span>
                      </label>
                      {picking && (
                        <span className="connect-surveys" role="group" aria-label={t("whichSurveys")}>
                          {surveyList.map((survey) => (
                            <label key={survey.id} className="connect-option">
                              <input
                                type="checkbox"
                                checked={surveyIds.includes(survey.id)}
                                onChange={(changed) => {
                                  setOnlySome(true);
                                  setSurveyIds((current) => (changed.target.checked ? [...current, survey.id] : current.filter((each) => each !== survey.id)));
                                }}
                              />
                              <span>{survey.title}</span>
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
                <span id={`${id}-sign`} className="connect-form__label">
                  {t("howSignsIn")}
                  <small>{t("howSignsInHint")}</small>
                </span>
                <span className="connect-form__stack">
                  <span role="radiogroup" aria-labelledby={`${id}-sign`} className="connect-segment">
                    {(
                      [
                        ["oauth", "oauthOption"],
                        ["api-key", "keyOption"],
                      ] as const
                    ).map(([value, label]) => (
                      <label key={value} data-on={signIn === value ? "" : undefined}>
                        <input type="radio" name={`${id}-sign`} checked={signIn === value} onChange={() => setSignIn(value)} />
                        {t(label)}
                      </label>
                    ))}
                  </span>
                  <small className="connection-muted">{t("signInHint")}</small>
                </span>
                <span id={`${id}-may`} className="connect-form__label">
                  {t("itMay")}
                </span>
                <span className="connect-form__stack">
                  <span role="group" aria-labelledby={`${id}-may`} className="connect-options">
                    {connectionPermissions.map((permission) => (
                      <label key={permission} className="connect-option">
                        <input
                          type="checkbox"
                          checked={shownPermissions.includes(permission)}
                          onChange={(changed) => {
                            setTouchedPermissions(true);
                            setPermissions(changed.target.checked ? connectionPermissions.filter((each) => each === permission || shownPermissions.includes(each)) : shownPermissions.filter((each) => each !== permission));
                          }}
                        />
                        <span>
                          {t(permissionKeys[permission].long)}
                          <small>{permissionKeys[permission].scopes}</small>
                        </span>
                      </label>
                    ))}
                  </span>
                  {errors.permissions !== undefined && <span className="designer-error">{t(errors.permissions)}</span>}
                  <small className="connection-muted">{t("permHint")}</small>
                </span>
              </>
            )}

            {people && (
              <>
                <span id={`${id}-match`} className="connect-form__label">
                  {t("matchBy")}
                </span>
                <span role="radiogroup" aria-labelledby={`${id}-match`} className="connect-options">
                  <label className="connect-option">
                    <input type="radio" name={`${id}-match`} checked={matchBy === "external-id"} onChange={() => setMatchBy("external-id")} />
                    <span>
                      {t("matchExternal")}
                      <small>{t("matchExternalHint")}</small>
                    </span>
                  </label>
                  <label className="connect-option">
                    <input type="radio" name={`${id}-match`} checked={matchBy === "email"} onChange={() => setMatchBy("email")} />
                    <span>{t("matchEmail")}</span>
                  </label>
                </span>
              </>
            )}

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
          </div>
          {errors.form !== undefined && (
            <p role="alert" className="designer-error connect-card__error">
              {t(errors.form)}
            </p>
          )}
          <div className="connect-foot">
            <button type="button" className="designer-button" onClick={() => setStep(0)}>
              {t("back")}
            </button>
            <span className="connection-grow" />
            <button type="button" className="designer-button designer-button--primary" disabled={busy} onClick={create}>
              {t("create")}
            </button>
          </div>
        </section>
      )}

      {step === 2 && created !== null && <TestStep t={t} client={client} created={created} baseUrl={baseUrl} writeClipboard={writeClipboard} pollMs={pollMs} now={now} onDone={() => onNavigate("/connections")} />}
    </main>
  );
}

/** Step 3: both directions tested live, the secrets once, and the code. */
function TestStep({ t, client, created, baseUrl, writeClipboard, pollMs, now, onDone }: { t: T; client: ConnectionsClient; created: { connection: ConnectionItem; credentials: ConnectionCredentials }; baseUrl: string; writeClipboard: (text: string) => Promise<void>; pollMs: number; now: () => Date; onDone: () => void }) {
  const [connection, setConnection] = useState(created.connection);
  const [sentAt, setSentAt] = useState<number | null>(null);
  const sentRef = useRef(sentAt);
  sentRef.current = sentAt;
  const { webhook, calls } = connection;
  const host = webhook === null ? "" : (shortUrl(webhook.url).split("/")[0] ?? "");
  const answered = sentAt !== null && webhook?.lastStatus != null;
  const delivered = answered && webhook?.lastDeliveredAt != null && Date.parse(webhook.lastDeliveredAt) >= sentAt - 1000;
  const guide = guideOf(connection);

  useEffect(() => {
    const timer = setInterval(() => void client.get(connection.id).then(setConnection, () => undefined), pollMs);

    return () => clearInterval(timer);
  }, [client, connection.id, pollMs]);

  const sendTest = () => client.test(connection.id).then(() => setSentAt(Date.now()), () => undefined);
  const status = webhook?.lastStatus === 200 ? "200 OK" : String(webhook?.lastStatus ?? "");

  return (
    <section className="connect-card connect-test">
      {webhook === null && calls === null && <p className="connection-muted">{t("nothingToTest")}</p>}
      <div className={webhook !== null && calls !== null ? "connect-two" : "connect-one"}>
        {webhook !== null && (
          <div className="connect-direction">
            <h2 className="connection-kicker">{t("toThem", { name: connection.name })}</h2>
            <div className="connect-box" data-state={delivered ? "done" : answered ? "failed" : "waiting"} role="status">
              {delivered && <b className="connect-box__mark" aria-hidden="true">✓</b>}
              <b>{delivered ? t("testDelivered") : answered ? t("testNotDelivered") : sentAt === null ? t("sendTest") : t("testWaiting", { host })}</b>
              <span>{delivered ? t("testAnswered", { host, status, ms: webhook.lastDurationMs ?? 0 }) : answered ? t("testRefused", { host, status }) : t("testNotSent", { url: shortUrl(webhook.url) })}</span>
            </div>
            <button type="button" className="designer-button connection-small" onClick={() => void sendTest()}>
              {sentAt === null ? t("sendTest") : t("sendAnother")}
            </button>
          </div>
        )}
        {calls !== null && (
          <div className="connect-direction">
            <h2 className="connection-kicker">{t("toUs", { name: connection.name })}</h2>
            <div className="connect-box" data-state={calls.lastCallAt !== null ? "done" : "waiting"} role="status">
              {calls.lastCallAt !== null && <b className="connect-box__mark" aria-hidden="true">✓</b>}
              <b>{calls.lastCallAt === null ? t("waitingCall") : t("calledTitle")}</b>
              <span>{calls.lastCallAt === null ? t("waitingCallHint") : t("calledAt", { when: ago(t, calls.lastCallAt, now()) })}</span>
            </div>
          </div>
        )}
      </div>
      <Credentials t={t} credentials={created.credentials} writeClipboard={writeClipboard} />
      {(calls !== null || webhook !== null) && <CodeTabs t={t} setup={setupOf(connection, created.credentials.clientId, baseUrl)} />}
      <div className="connect-foot connect-foot--plain">
        {(calls !== null || webhook !== null) && (
          <a className="designer-button" href={`/developers?guide=${guide.id}`} target="_blank" rel="noreferrer">
            {t(guide.label)}
          </a>
        )}
        <span className="connection-grow" />
        <button type="button" className="designer-button designer-button--primary" onClick={onDone}>
          {t("done")}
        </button>
      </div>
    </section>
  );
}
