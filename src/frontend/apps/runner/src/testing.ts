// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written RunClient for the runner's tests: answers are set up per test, calls are recorded.
import { ApiError, type Answers, type RunClient, type RunResponse, type RunSurvey } from "@askwerk/app-client";
import type { SurveyDefinition, ThemeTokens } from "@askwerk/schema";

const both = (de: string, en: string) => ({ de, en });

export const sampleDefinition: SurveyDefinition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "de",
  welcome: { title: both("Kundenzufriedenheit 2026", "Customer satisfaction 2026"), text: both("Danke, dass Sie mitmachen.", "Thank you for taking part.") },
  endPage: { text: both("Danke! Ihre Antworten sind gespeichert.", "Thank you! Your answers are saved.") },
  pages: [
    {
      id: "p1",
      title: both("Ihr Besuch", "Your visit"),
      questions: [
        { id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5, minLabel: both("Sehr unzufrieden", "Very unsatisfied"), maxLabel: both("Sehr zufrieden", "Very satisfied") } },
        {
          id: "q2",
          variable: "SOURCE",
          type: "multiple",
          text: both("Wie haben Sie von uns erfahren?", "How did you hear about us?"),
          required: false,
          options: [
            { code: "1", label: both("Suchmaschine", "Search engine") },
            { code: "2", label: both("Empfehlung", "Recommendation") },
          ],
          other: { code: "OTHER", label: both("Sonstiges, bitte angeben:", "Other, please specify:"), variable: "SOURCE_OTHER" },
        },
      ],
    },
    {
      id: "p2",
      questions: [
        { id: "q3", variable: "BEFORE", type: "yes-no", text: both("Waren Sie schon einmal hier?", "Have you been here before?"), required: true },
        { id: "q4", variable: "EMAIL", type: "short-text", format: "email", text: both("Ihre E-Mail-Adresse", "Your email address"), required: false },
      ],
    },
  ],
};

export const traceId = "4bf92f3577b34da6a3ce929d0e0e4736";

export function fieldErrors(errors: Record<string, string[]>): ApiError {
  return new ApiError(400, traceId, "invalid", { errors });
}

type Outcome<T> = T | ApiError;

export class FakeRunClient implements RunClient {
  readonly calls: [string, ...unknown[]][] = [];
  survey: RunSurvey<SurveyDefinition> | null;
  saveAnswers: Outcome<void>[] = [undefined];
  completeAnswers: Outcome<void>[] = [undefined];

  constructor(definition: SurveyDefinition = sampleDefinition, response: RunResponse | null = null, state: "open" | "closed" = "open", theme?: ThemeTokens) {
    this.survey = { survey: { id: "s1", organisation: "Acme GmbH", state, definition, ...(theme !== undefined && { theme }) }, response };
  }

  /** Changes what opening the link gives, e.g. a closing date or one response per person. */
  change(survey: Partial<RunSurvey<SurveyDefinition>["survey"]>) {
    this.survey = { ...this.survey!, survey: { ...this.survey!.survey, ...survey } };
  }

  assetUrl = (surveyId: string, assetId: string, width?: number) => `/api/app/run/${surveyId}/assets/${assetId}${width === undefined ? "" : `?w=${width}`}`;

  open = <T,>(surveyId: string) => {
    this.calls.push(["open", surveyId]);
    return Promise.resolve(this.survey as RunSurvey<T> | null);
  };

  start = (surveyId: string, language: string) => {
    this.calls.push(["start", surveyId, language]);
    const first = this.survey?.survey.definition.pages[0]?.id ?? "p1";
    return Promise.resolve<RunResponse>({ status: "partial", pageId: first, language, answers: {} });
  };

  savePage = (surveyId: string, pageId: string, answers: Answers) => {
    this.calls.push(["savePage", surveyId, pageId, answers]);
    return next(this.saveAnswers);
  };

  complete = (surveyId: string) => {
    this.calls.push(["complete", surveyId]);
    return next(this.completeAnswers);
  };
}

function next(answers: Outcome<void>[]): Promise<void> {
  const answer = answers.length > 1 ? answers.shift() : answers[0];

  return answer instanceof ApiError ? Promise.reject(answer) : Promise.resolve();
}
