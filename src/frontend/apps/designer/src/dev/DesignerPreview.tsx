// SPDX-License-Identifier: AGPL-3.0-only
// Development only (/dev/designer): the designer on the sample survey and an in-memory API, until the App API
// endpoints exist (AWK-15, step 3). Never part of a production build: main.tsx loads it behind import.meta.env.DEV.
// ?lang=de|en picks the language; ?scenario=empty|save-fails|publish-fails shows the other states of M2 and M3;
// ?scenario=types shows the question types of W7 (AWK-20); ?scenario=large has 150 questions for measuring.
import { ApiError } from "@askwerk/app-client";
import { pickLocale, type SurveyDefinition } from "@askwerk/schema";
import { Designer } from "../designer/Designer";
import { FakeDesignerApi, sampleDefinition, sampleDraft, samplePublished, sequentialIds } from "../designer/testing";

const both = (de: string, en: string) => ({ de, en });

/** One question of each type and look added in AWK-20 (W7 rev. 4). */
const typesDefinition: SurveyDefinition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "en",
  pages: [
    {
      id: "p1",
      title: both("Neue Fragetypen", "New question types"),
      questions: [
        { id: "t1", variable: "BRANCH", type: "dropdown", text: both("Welche Filiale haben Sie besucht?", "Which branch did you visit?"), required: true, options: [{ code: "1", label: both("Wien Mitte", "Vienna Centre") }, { code: "2", label: both("Graz", "Graz") }] },
        { id: "t2", variable: "SOURCE", type: "multiple", text: both("Wie haben Sie von uns erfahren?", "How did you hear about us?"), required: false, options: [{ code: "1", label: both("Suchmaschine", "Search engine") }, { code: "2", label: both("Empfehlung", "Recommendation") }], other: { code: "OTHER", label: both("Sonstiges, bitte angeben:", "Other, please specify:"), variable: "SOURCE_OTHER" } },
        { id: "t3", variable: "BEFORE", type: "yes-no", text: both("Waren Sie schon einmal hier?", "Have you been here before?"), required: true },
        { id: "t4", variable: "LIKELY", type: "slider", text: both("Wie wahrscheinlich kommen Sie wieder?", "How likely are you to come back?"), required: false, scale: { min: 0, max: 100, step: 5, minLabel: both("unwahrscheinlich", "not likely"), maxLabel: both("sehr wahrscheinlich", "very likely"), showValue: true } },
        { id: "t5", variable: "EVENT", type: "rating", display: "stars", text: both("Wie bewerten Sie die Veranstaltung?", "How do you rate the event?"), required: false, scale: { min: 1, max: 10 } },
        { id: "t6", variable: "MOOD", type: "rating", display: "faces", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: false, scale: { min: 1, max: 5 } },
        { id: "t7", variable: "EMAIL", type: "short-text", format: "email", placeholder: both("name@firma.at", "name@example.com"), text: both("Ihre E-Mail-Adresse", "Your email address"), required: false },
        { id: "t8", variable: "STARTED", type: "date", mode: "date-time", minuteStep: 15, text: both("Wann hat das Problem begonnen?", "When did the problem start?"), required: false },
      ],
    },
  ],
};

/** 10 pages × 15 questions in two languages, to measure how the designer feels with a real-sized survey. */
function largeDefinition(): SurveyDefinition {
  return {
    schemaVersion: 1,
    languages: ["de", "en"],
    defaultLanguage: "en",
    pages: Array.from({ length: 10 }, (_, page) => ({
      id: `p${page + 1}`,
      title: both(`Seite ${page + 1}`, `Page ${page + 1}`),
      questions: Array.from({ length: 15 }, (_, index) => {
        const n = page * 15 + index + 1;
        return {
          id: `q${n}`,
          variable: `Q${n}`,
          type: "single" as const,
          text: both(`Frage ${n}`, `Question ${n}`),
          required: false,
          options: [1, 2, 3, 4, 5].map((code) => ({ code: String(code), label: both(`Antwort ${code}`, `Answer ${code}`) })),
        };
      }),
    })),
  };
}

const failure = new ApiError(503, "4bf92f3577b34da6a3ce929d0e0e4736", "failed");

function apiFor(scenario: string | null) {
  switch (scenario) {
    case "empty":
      return new FakeDesignerApi({ ...sampleDraft, title: "Neue Umfrage", definition: { ...sampleDefinition, pages: [{ id: "p1", questions: [] }] } });
    case "large":
      return new FakeDesignerApi({ ...sampleDraft, title: "Large survey", definition: largeDefinition() });
    case "types":
      return new FakeDesignerApi({ ...sampleDraft, title: "Neue Fragetypen", definition: typesDefinition });
    case "save-fails":
      return new FakeDesignerApi(sampleDraft, [failure]);
    case "publish-fails":
      return new FakeDesignerApi({ ...sampleDraft, published: sampleDefinition }, ["ok"], [failure]);
    default:
      return new FakeDesignerApi(sampleDraft, ["ok"], [samplePublished]);
  }
}

export function DesignerPreview() {
  const parameters = new URLSearchParams(location.search);
  const requested = parameters.get("lang");
  const locale = requested === "de" || requested === "en" ? requested : pickLocale(navigator.languages);

  return <Designer api={apiFor(parameters.get("scenario"))} surveyId="s1" initialLocale={locale} newId={sequentialIds("dev")} />;
}
