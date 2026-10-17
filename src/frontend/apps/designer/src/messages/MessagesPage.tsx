// SPDX-License-Identifier: AGPL-3.0-only
import "../look/look.css";
import "./messages.css";
import { DeviceSwitch, deviceWidth, ZoomBox, type Device } from "../designer/ZoomBox";
import { ApiError, type EmailPreview, type MessageKind, type MessageText, type MessagesClient, type Texts } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox } from "@askwerk/ui";
import { useEffect, useId, useMemo, useRef, useState } from "react";
import { messagesMessages, type MessagesMessageKey } from "./messages";

type T = (key: MessagesMessageKey, values?: Record<string, string | number>) => string;

type MessagesPageProps = {
  client: MessagesClient;
  /** Surveys the preview can show; the first is shown at the start. */
  surveys: readonly { id: string; title: string }[];
  locale: Locale;
  /** Pause after the last change before saving and previewing; tests use 0. */
  saveDelayMs?: number;
};

const kinds: readonly (readonly [MessageKind, MessagesMessageKey])[] = [
  ["invitation", "invitation"],
  ["reminder", "reminder"],
  ["thanks", "thanks"],
];

const placeholders = ["name", "link", "deadline", "duration", "organisation", "survey"] as const;
const languages = ["de", "en"] as const;

/** Placeholders in a text that Askwerk does not fill in. */
const unknownIn = (text: string) => [...text.matchAll(/\{([a-zA-Z]+)\}/g)].map((match) => match[1]!).filter((name) => !(placeholders as readonly string[]).includes(name));

/**
 * Organisation → Messages (AWK-17 step 6, W10 D), stupid simple: the three message texts per language with one-click
 * placeholders, saved by themselves, a live preview from the server's email renderer and a test send to oneself.
 */
export function MessagesPage({ client, surveys, locale, saveDelayMs = 600 }: MessagesPageProps) {
  const t: T = useMemo(() => createTranslator(messagesMessages, locale), [locale]);
  const [messages, setMessages] = useState<MessageText[] | null>(null);
  const [kind, setKind] = useState<MessageKind>("invitation");

  useEffect(() => {
    client.list().then(setMessages, () => setMessages([]));
  }, [client]);

  const message = messages?.find((each) => each.kind === kind);

  return (
    <main className="look" lang={locale}>
      <h1>{t("title")}</h1>
      <p className="look-lead">{t("lead")}</p>
      {messages === null ? (
        <p role="status" aria-busy="true">
          {t("loading")}
        </p>
      ) : (
        <div className="look-work">
          <nav className="look-list" aria-label={t("title")}>
            {kinds.map(([each, label]) => (
              <button key={each} type="button" className="look-list__theme" aria-current={each === kind ? "true" : undefined} onClick={() => setKind(each)}>
                <span>{t(label)}</span>
              </button>
            ))}
          </nav>
          {message !== undefined && (
            <Editor
              key={kind}
              t={t}
              locale={locale}
              client={client}
              message={message}
              surveys={surveys}
              saveDelayMs={saveDelayMs}
              onSaved={(saved) => setMessages((current) => current?.map((each) => (each.kind === kind ? { ...each, ...saved, isDefault: false } : each)) ?? null)}
            />
          )}
        </div>
      )}
    </main>
  );
}

type EditorProps = {
  t: T;
  locale: Locale;
  client: MessagesClient;
  message: MessageText;
  surveys: MessagesPageProps["surveys"];
  saveDelayMs: number;
  onSaved: (saved: { subject: Texts; body: Texts; answerInEmail: boolean }) => void;
};

function Editor({ t, locale, client, message, surveys, saveDelayMs, onSaved }: EditorProps) {
  const [language, setLanguage] = useState<string>(languages.includes(locale as "de") ? locale : "de");
  const [subject, setSubject] = useState<Texts>(message.subject);
  const [body, setBody] = useState<Texts>(message.body);
  const [answerInEmail, setAnswerInEmail] = useState(message.answerInEmail);
  const [status, setStatus] = useState<"saved" | "saving" | "failed">("saved");
  const first = useRef(true);
  const latestSaved = useRef(onSaved);
  latestSaved.current = onSaved;
  const unknown = [...new Set([...Object.values(subject), ...Object.values(body)].flatMap(unknownIn))];
  const id = useId();

  useEffect(() => {
    if (first.current) {
      first.current = false;
      return undefined;
    }

    if ([...Object.values(subject), ...Object.values(body)].some((text) => unknownIn(text).length > 0)) {
      return undefined;
    }

    setStatus("saving");
    const timer = setTimeout(() => {
      const saved = { subject, body, answerInEmail };
      client.save(message.kind, saved).then(
        () => {
          setStatus("saved");
          latestSaved.current(saved);
        },
        () => setStatus("failed"),
      );
    }, saveDelayMs);

    return () => clearTimeout(timer);
  }, [client, message.kind, subject, body, answerInEmail, saveDelayMs]);

  const asks = message.kind !== "thanks";

  return (
    <>
      <section className="look-editor" aria-label={t("message")}>
        <span className="designer-segment look-row" role="group" aria-label={t("language")}>
          {languages.map((each) => (
            <button key={each} type="button" className={each === language ? "look-button look-button--on" : "look-button"} aria-pressed={each === language} onClick={() => setLanguage(each)}>
              {each.toUpperCase()}
            </button>
          ))}
        </span>
        {message.isDefault && status === "saved" && <p className="look-note">{t("askwerkText")}</p>}
        <div className="look-field">
          <label htmlFor={`${id}-subject`}>{t("subject")}</label>
          <input id={`${id}-subject`} className="look-input" value={subject[language] ?? ""} onChange={(event) => setSubject({ ...subject, [language]: event.target.value })} />
        </div>
        <div className="look-field">
          <label htmlFor={`${id}-body`}>{t("text")}</label>
          <textarea id={`${id}-body`} className="look-input messages-body" rows={10} value={body[language] ?? ""} onChange={(event) => setBody({ ...body, [language]: event.target.value })} />
        </div>
        <div className="look-field">
          <span className="look-label">{t("placeholders")}</span>
          <div className="look-row">
            {placeholders.map((name) => (
              <button key={name} type="button" className="messages-chip" onClick={() => setBody({ ...body, [language]: `${body[language] ?? ""}{${name}}` })}>
                {`{${name}}`}
              </button>
            ))}
          </div>
        </div>
        {unknown.length > 0 && (
          <p className="look-error" role="alert">
            {t("unknownPlaceholder", { found: `{${unknown[0]}}` })}
          </p>
        )}
        {asks && (
          <label className="messages-switch">
            <input type="checkbox" checked={answerInEmail} onChange={(event) => setAnswerInEmail(event.target.checked)} />
            {t("answerInEmail")}
          </label>
        )}
        <span className="look-status" role="status">
          {unknown.length === 0 && (status === "saved" ? t("saved") : status === "saving" ? t("saving") : t("saveFailed"))}
        </span>
      </section>
      <Preview t={t} client={client} kind={message.kind} surveys={surveys} language={language} subject={subject} body={body} answerInEmail={answerInEmail} delayMs={saveDelayMs} />
    </>
  );
}

type PreviewProps = {
  t: T;
  client: MessagesClient;
  kind: MessageKind;
  surveys: MessagesPageProps["surveys"];
  language: string;
  subject: Texts;
  body: Texts;
  answerInEmail: boolean;
  delayMs: number;
};

/** The email as the server draws it, in a sandbox, for a chosen survey; and a test send to oneself. */
function Preview({ t, client, kind, surveys, language, subject, body, answerInEmail, delayMs }: PreviewProps) {
  const [surveyId, setSurveyId] = useState(surveys[0]?.id ?? null);
  const [email, setEmail] = useState<EmailPreview | null>(null);
  const [sent, setSent] = useState<string | null>(null);
  const [device, setDevice] = useState<Device>("desktop");
  const label = useId();

  useEffect(() => {
    if (surveyId === null) {
      return undefined;
    }

    const timer = setTimeout(() => {
      client.preview(kind, { surveyId, language, subject, body, answerInEmail }).then(setEmail, () => setEmail(null));
    }, delayMs);

    return () => clearTimeout(timer);
  }, [client, kind, surveyId, language, subject, body, answerInEmail, delayMs]);

  async function sendTest() {
    if (surveyId === null) {
      return;
    }

    setSent(null);

    try {
      await client.sendTest(kind, { surveyId, language });
      setSent(t("testSent"));
    } catch (error) {
      setSent(error instanceof ApiError && error.problem.code === "email-not-set-up" ? t("emailNotSetUp") : t("testFailed"));
    }
  }

  return (
    <section className="look-preview-region messages-preview" aria-label={t("preview")}>
      {surveyId === null ? (
        <p className="look-note">{t("noSurvey")}</p>
      ) : (
        <>
          <div className="look-field">
            <span className="look-label" id={label}>
              {t("previewWith")}
            </span>
            <ComboBox
              labelledBy={label}
              options={surveys.map((survey) => ({ value: survey.id, label: survey.title }))}
              value={surveyId}
              onChange={setSurveyId}
              texts={{ choose: t("previewWith"), search: t("search"), results: (shown, total) => t("results", { shown, total }), noResults: t("noResults") }}
            />
          </div>
          {email !== null && (
            <>
              <p className="messages-subject">{email.subject}</p>
              {/* Sandboxed: the email's HTML never runs or reaches the app. */}
              <ZoomBox
                texts={{ zoom: t("zoom"), zoomIn: t("zoomIn"), zoomOut: t("zoomOut"), fit: t("fit") }}
                bar={<DeviceSwitch label={t("width")} texts={{ phone: t("phone"), tablet: t("tablet"), desktop: t("desktop") }} value={device} onChange={setDevice} />}
                width={deviceWidth(device)}
              >
                <iframe className="messages-frame" title={t("emailPreview")} sandbox="" srcDoc={email.html} />
              </ZoomBox>
            </>
          )}
          <button type="button" className="look-button" onClick={() => void sendTest()}>
            {t("sendTest")}
          </button>
          {sent !== null && (
            <p className="look-note" role="status">
              {sent}
            </p>
          )}
        </>
      )}
    </section>
  );
}
