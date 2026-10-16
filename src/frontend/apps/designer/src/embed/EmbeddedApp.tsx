// SPDX-License-Identifier: AGPL-3.0-only
import {
  ApiError,
  createMessagesClient,
  createParticipantsClient,
  createResultsClient,
  createSurveysClient,
  createThemesClient,
  renewEmbedSession,
  startEmbedSession,
  type AppClientOptions,
  type EmbedSession,
} from "@askwerk/app-client";
import { createTranslator, pickLocale, type Locale } from "@askwerk/schema";
import { useEffect, useMemo, useRef, useState } from "react";
import { designerApi, type DesignerApi } from "../designer/api";
import { Designer } from "../designer/Designer";
import { ResultsPage } from "../results/ResultsPage";
import { ParticipantsPage, SharePage, type SurveyTab } from "../workspace";
import { embedMessages, type EmbedMessageKey } from "./messages";

/** What the frame tells the host page (W21 1). */
export type HostEvent =
  | { type: "askwerk:ready"; surveyId: string; view: string }
  | { type: "askwerk:saved"; surveyId: string }
  | { type: "askwerk:published"; surveyId: string; version: number }
  | { type: "askwerk:resize"; height: number }
  | { type: "askwerk:navigate"; to: "list" | SurveyTab; surveyId: string }
  | { type: "askwerk:session-ending"; endsAt: string }
  | { type: "askwerk:error"; code: string };

type EmbeddedAppProps = {
  /** The host's hand-over token from the frame's address; used once. */
  token: string;
  view: "designer" | "results";
  /** ?lang= from the host; wins over the token's locale and the browser's. */
  lang: string | null;
  telemetry: AppClientOptions["telemetry"];
  post: (event: HostEvent) => void;
  start?: (options: AppClientOptions, token: string, view: "designer" | "results") => Promise<EmbedSession>;
  renew?: (options: AppClientOptions) => Promise<{ expiresAt: string; endsAt: string }>;
  now?: () => number;
  saveFile?: (name: string, file: Blob) => void;
};

const renewEvery = 45 * 60 * 1000;
const warnBefore = 2 * 60 * 1000;

/** Saved and published go to the host as events. */
function withHostEvents(api: DesignerApi, post: (event: HostEvent) => void): DesignerApi {
  return {
    ...api,
    saveDraft: (surveyId, definition) => api.saveDraft(surveyId, definition).then(() => post({ type: "askwerk:saved", surveyId })),
    publish: (surveyId) =>
      api.publish(surveyId).then((published) => {
        post({ type: "askwerk:published", surveyId, version: published.versionNumber });
        return published;
      }),
  };
}

/**
 * The designer or Results inside a host product (AWK-13, W21): no Askwerk bar, survey list or account menu; the host
 * owns the navigation. The session's bearer token lives in memory only, renewed while the frame is open.
 */
export function EmbeddedApp({ token, view, lang, telemetry, post, start = startEmbedSession, renew = renewEmbedSession, now = () => Date.now(), saveFile = () => undefined }: EmbeddedAppProps) {
  const [session, setSession] = useState<EmbedSession | null>(null);
  const [problem, setProblem] = useState<EmbedMessageKey | null>(null);
  const [ending, setEnding] = useState(false);
  const [tab, setTab] = useState<SurveyTab>("build");
  const bearer = useRef<string>("");
  const locale: Locale = pickLocale([lang ?? session?.language ?? "", ...navigator.languages]);
  const t = useMemo(() => createTranslator(embedMessages, locale), [locale]);

  useEffect(() => {
    start({ telemetry }, token, view).then(
      (started) => {
        bearer.current = started.token;
        setSession(started);
        setTab(started.view === "results" ? "results" : "build");
        post({ type: "askwerk:ready", surveyId: started.surveyId, view: started.view });
      },
      (failure: unknown) => {
        const code = failure instanceof ApiError && typeof failure.problem.code === "string" && failure.problem.code in embedMessages.en ? (failure.problem.code as EmbedMessageKey) : "failed";
        setProblem(code);
        post({ type: "askwerk:error", code });
      },
    );
  }, []);

  const options: AppClientOptions = useMemo(() => ({ telemetry, headers: () => ({ Authorization: `Bearer ${bearer.current}` }) }), [telemetry]);

  // Renewed while open; the host hears two minutes before the end.
  useEffect(() => {
    if (session === null) {
      return undefined;
    }

    const renewal = setInterval(() => void renew(options).catch(() => undefined), renewEvery);
    const warning = setTimeout(
      () => {
        setEnding(true);
        post({ type: "askwerk:session-ending", endsAt: session.endsAt });
      },
      Math.max(0, Date.parse(session.endsAt) - warnBefore - now()),
    );

    return () => {
      clearInterval(renewal);
      clearTimeout(warning);
    };
  }, [session]);

  useEffect(() => {
    if (typeof ResizeObserver === "undefined") {
      return undefined;
    }

    const observer = new ResizeObserver(() => post({ type: "askwerk:resize", height: document.documentElement.scrollHeight }));
    observer.observe(document.body);

    return () => observer.disconnect();
  }, []);

  const clients = useMemo(() => {
    const surveys = createSurveysClient(options);
    const themes = createThemesClient(options);

    return {
      surveys,
      designer: withHostEvents(designerApi(surveys, themes), post),
      results: createResultsClient(options),
      participants: createParticipantsClient(options),
      messages: createMessagesClient(options),
    };
  }, [options]);

  if (problem !== null) {
    return (
      <main className="embed-state" lang={locale}>
        <p role="alert">{t(problem)}</p>
      </main>
    );
  }

  if (session === null) {
    return (
      <main className="embed-state" lang={locale} aria-busy="true">
        <p>{t("starting")}</p>
      </main>
    );
  }

  const surveyId = session.surveyId;
  const canEdit = session.view === "designer";
  const navigation = {
    surveys: clients.surveys,
    surveyId,
    locale,
    canEdit,
    onTab: (next: SurveyTab) => setTab(next === "settings" ? "build" : next),
    onBack: () => post({ type: "askwerk:navigate", to: "list", surveyId }),
    onOpenSurvey: (other: string) => post({ type: "askwerk:navigate", to: "build", surveyId: other }),
  };
  const preview = (language: string) => clients.messages.preview("invitation", { surveyId, language }).then(({ subject, text }) => ({ subject, text }));

  return (
    <>
      {ending && (
        <p className="embed-notice" role="status">
          {t("ending")}
        </p>
      )}
      {tab === "participants" ? (
        <ParticipantsPage {...navigation} participants={clients.participants} preview={preview} />
      ) : tab === "share" && canEdit ? (
        <SharePage {...navigation} participants={clients.participants} preview={preview} saveFile={saveFile} />
      ) : tab === "build" && canEdit ? (
        <Designer api={clients.designer} surveyId={surveyId} initialLocale={locale} newId={() => crypto.randomUUID()} onBack={navigation.onBack} onTab={navigation.onTab} onOpenSurvey={navigation.onOpenSurvey} participants={clients.participants} previewInvitation={preview} saveFile={saveFile} />
      ) : (
        <ResultsPage client={clients.results} {...navigation} surveyLink={`${location.origin}/s/${encodeURIComponent(surveyId)}`} />
      )}
    </>
  );
}
