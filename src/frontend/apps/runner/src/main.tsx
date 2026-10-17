// SPDX-License-Identifier: AGPL-3.0-only
import "@askwerk/ui/tokens.css";
import { createPreviewClient, createRunClient, surveyIdOf, unsubscribe, unsubscribeInfo } from "@askwerk/app-client";
import { pickLocale } from "@askwerk/schema";
import { beaconTransport, createTelemetry, installGlobalErrorHandlers, observeWebVitals } from "@askwerk/telemetry";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { postHeight, withEmbedEvents, type EmbedEvent } from "./embed";
import { readHostLook } from "@askwerk/ui";
import { HostStyle, Runner } from "./Runner";
import { Unsubscribe } from "./Unsubscribe";

const telemetry = createTelemetry({ service: "runner", transport: beaconTransport(), route: () => "/s/:survey" });
installGlobalErrorHandlers(window, telemetry);
observeWebVitals(PerformanceObserver, telemetry);

// /s/u/{token}: the unsubscribe link of an invitation (AWK-7).
const unsubscribeToken = /^\/s\/u\/([a-z0-9]+)$/.exec(location.pathname)?.[1];

if (unsubscribeToken !== undefined) {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <Unsubscribe
        load={() => unsubscribeInfo({ telemetry }, unsubscribeToken)}
        confirm={() => unsubscribe({ telemetry }, unsubscribeToken)}
        initialLocale={pickLocale(navigator.languages)}
      />
    </StrictMode>,
  );
} else {
  await runSurvey();
}

async function runSurvey() {
  // The link is /s/{code} (AWK-7), or /s/{survey id} as before.
  const segment = decodeURIComponent(location.pathname.replace(/^\/s\/?/, "").split("/")[0] ?? "");
  // /s/{survey}?preview is the editor's preview of the draft (AWK-22): nothing is stored.
  const query = new URLSearchParams(location.search);
  const preview = query.has("preview");
  const surveyId = preview ? segment : await surveyIdOf({ telemetry }, segment);
  // In a frame on another website (AWK-7, embed.js or an iframe): events and height go to that page.
  const embedded = !preview && window.self !== window.top;
  const post = (event: EmbedEvent) => window.parent.postMessage(event, "*");
  // ?t={token}: an invited person's personal link (AWK-7). ?identity={token}: a host product names the respondent
  // (AWK-13); it is used once, so it leaves the address at once.
  const identity = query.get("identity") ?? undefined;

  if (identity !== undefined) {
    query.delete("identity");
    history.replaceState(history.state, "", `${location.pathname}${query.size > 0 ? `?${query.toString()}` : ""}`);
  }

  const runClient = createRunClient({ telemetry }, query.get("t") ?? undefined, identity);

  if (embedded) {
    postHeight(segment, post);
  }

  // ?lang=de from the embed code, else the browser's languages.
  const language = query.get("lang") ?? pickLocale(navigator.languages);
  // ?answer={question}:{value}: an answer clicked in the invitation email (AWK-17 step 6).
  const [answerQuestion, answerValue] = (query.get("answer") ?? "").split(":");

  // <askwerk-survey look="host"> (W37): the host app's accent, background, font and corners, from #look=….
  const hostLook = embedded ? readHostLook(location.hash) : null;

  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <HostStyle.Provider value={hostLook}>
      <Runner
        client={preview ? createPreviewClient({ telemetry }) : embedded ? withEmbedEvents(runClient, segment, post) : runClient}
        // In the designer's side panel, the panel already says so; a tab of its own shows the banner (M5 B, C).
        preview={preview && window.self === window.top}
        liveLook={preview && window.self !== window.top}
        {...(answerQuestion && answerValue !== undefined && { initialAnswer: { questionId: answerQuestion, value: answerValue } })}
        surveyId={surveyId}
        initialLocale={language}
        reducedMotion={matchMedia("(prefers-reduced-motion: reduce)").matches}
      />
      </HostStyle.Provider>
    </StrictMode>,
  );
}
