// SPDX-License-Identifier: AGPL-3.0-only
// Test helpers for the designer: the M1 sample survey and a hand-written in-memory DesignerApi (no mocking library).
import { ApiError, type SurveyApprovals, type SurveyLook, type SurveySummary } from "@askwerk/app-client";
import { themePresets, type SurveyDefinition, type ThemeTokens } from "@askwerk/schema";
import type { DesignerApi, PublishedSurvey, SurveyDraft, ThemeChoice } from "./api";

/** The survey shown in the approved mockups M1–M3 (AWK-5). */
export const sampleDefinition: SurveyDefinition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "en",
  pages: [
    {
      id: "p1",
      title: { de: "Willkommen", en: "Welcome" },
      questions: [
        {
          id: "q1",
          variable: "INTRO",
          type: "text-block",
          text: { de: "Willkommen. Die Umfrage dauert etwa 3 Minuten.", en: "Welcome. The survey takes about 3 minutes." },
          required: false,
        },
      ],
    },
    {
      id: "p2",
      title: { de: "Ihr Besuch", en: "Your visit" },
      questions: [
        {
          id: "q2",
          variable: "SAT_1",
          type: "rating",
          text: { de: "Wie zufrieden sind Sie mit unserem Service?", en: "How satisfied are you with our service?" },
          help: { de: "Bitte denken Sie an Ihren letzten Besuch.", en: "Please think about your last visit." },
          required: true,
          scale: { min: 1, max: 5, minLabel: { de: "Sehr unzufrieden", en: "Very unsatisfied" }, maxLabel: { de: "Sehr zufrieden", en: "Very satisfied" } },
        },
        { id: "q3", variable: "IMPROVE", type: "long-text", text: { en: "What could we improve?" }, required: false },
        { id: "q4", variable: "NPS", type: "nps", text: { de: "Würden Sie uns empfehlen?", en: "Would you recommend us?" }, required: false },
      ],
    },
    {
      id: "p3",
      title: { de: "Über Sie", en: "About you" },
      questions: [
        {
          id: "q5",
          variable: "BRANCH",
          type: "single",
          text: { de: "Welche Filiale?", en: "Which branch?" },
          required: false,
          options: [
            { code: "1", label: { de: "Wien", en: "Vienna" } },
            { code: "2", label: { de: "Graz", en: "Graz" } },
          ],
        },
        { id: "q6", variable: "AGE", type: "short-text", text: { en: "Age group" }, required: false },
      ],
    },
  ],
  endPage: { text: { en: "Thank you." } },
};

export const sampleDraft: SurveyDraft = {
  surveyId: "s1",
  title: "Customer satisfaction 2026",
  versionNumber: 3,
  definition: sampleDefinition,
  published: null,
};

export const samplePublished: PublishedSurvey = {
  versionNumber: 3,
  links: [
    { kind: "domain", hostname: "surveys.acme.at", isPrimary: true, url: "https://surveys.acme.at/s/kz26b7mq" },
    { kind: "system", hostname: "acme.askwerk.app", isPrimary: false, url: "https://acme.askwerk.app/s/kz26b7mq" },
  ],
};

type Outcome<T> = T | ApiError | "pending";

/** An in-memory DesignerApi whose answers a test sets up; it records what was saved. */
export const sampleThemes: ThemeChoice[] = [
  { id: "t1", name: "Acme Standard", isDefault: true },
  { id: "t2", name: "Acme Event", isDefault: false },
];

export class FakeDesignerApi implements DesignerApi {
  readonly saved: SurveyDefinition[] = [];
  publishCalls = 0;
  readonly themeCalls: (string | null)[] = [];
  readonly uploads: string[] = [];
  readonly templates: string[] = [];
  readonly renamed: string[] = [];
  readonly menuCalls: string[] = [];
  readonly approved: (string | null)[] = [];
  private approvalList: { id: string; at: string; note: string | null; fingerprint: string }[] = [];
  themes: ThemeChoice[] = sampleThemes;
  /** The survey's look (W26): the organisation's default until it gets its own. */
  storedLook: SurveyLook<ThemeTokens> = { tokens: themePresets[0]!.tokens, own: false, lookBase: null, themeId: null };
  readonly lookCalls: { tokens: ThemeTokens; lookBase: string | null }[] = [];
  readonly savedThemes: string[] = [];
  saveThemeOutcome: "ok" | ApiError = "ok";

  constructor(
    private draft: Outcome<SurveyDraft> = sampleDraft,
    private readonly saveOutcomes: ("ok" | ApiError)[] = ["ok"],
    private readonly publishOutcomes: Outcome<PublishedSurvey>[] = [samplePublished],
  ) {}

  summary(): Promise<SurveySummary> {
    if (typeof this.draft !== "object" || this.draft instanceof Error) {
      return new Promise(() => undefined);
    }

    const { surveyId, title, definition, published, versionNumber } = this.draft;

    return Promise.resolve({
      id: surveyId,
      title: this.renamed.at(-1) ?? title,
      status: published === null ? "draft" : "published",
      publishedVersion: published === null ? null : versionNumber,
      hasChanges: published !== null && published !== definition,
      languages: [...definition.languages],
      responses: 0,
      participants: 0,
    });
  }

  /** The saved draft as the fingerprint: an approval holds until the next save. */
  private fingerprint(): string {
    const saved = this.saved.at(-1);
    return JSON.stringify(saved ?? (typeof this.draft === "object" && !(this.draft instanceof Error) ? this.draft.definition : null));
  }

  approvals(): Promise<SurveyApprovals> {
    const fingerprint = this.fingerprint();
    const items = [...this.approvalList].reverse().map((item) => ({ ...item, by: "Anna Berger", version: 3, current: item.fingerprint === fingerprint, mine: true }));

    return Promise.resolve({ fingerprint, state: items.some((item) => item.current) ? "approved" : items.length > 0 ? "earlier" : "none", items });
  }

  approve(_surveyId: string, note: string | null): Promise<string> {
    this.approved.push(note);
    this.approvalList.push({ id: `a${this.approved.length}`, at: new Date().toISOString(), note, fingerprint: this.fingerprint() });
    return Promise.resolve(`a${this.approved.length}`);
  }

  withdrawApproval(_surveyId: string, approvalId: string): Promise<void> {
    this.approvalList = this.approvalList.filter((item) => item.id !== approvalId);
    return Promise.resolve();
  }

  rename(_surveyId: string, title: string): Promise<void> {
    this.renamed.push(title);
    return Promise.resolve();
  }

  duplicate(): Promise<string> {
    this.menuCalls.push("duplicate");
    return Promise.resolve("s2");
  }

  close(): Promise<void> {
    this.menuCalls.push("close");
    return Promise.resolve();
  }

  reopen(): Promise<void> {
    this.menuCalls.push("reopen");
    return Promise.resolve();
  }

  archive(): Promise<void> {
    this.menuCalls.push("archive");
    return Promise.resolve();
  }

  loadDraft(): Promise<SurveyDraft> {
    return settle(this.draft);
  }

  saveDraft(_surveyId: string, definition: SurveyDefinition): Promise<void> {
    this.saved.push(definition);
    const outcome = this.saveOutcomes.length > 1 ? this.saveOutcomes.shift()! : this.saveOutcomes[0]!;

    return outcome === "ok" ? Promise.resolve() : Promise.reject(outcome);
  }

  publish(): Promise<PublishedSurvey> {
    this.publishCalls += 1;
    const outcome = this.publishOutcomes.length > 1 ? this.publishOutcomes.shift()! : this.publishOutcomes[0]!;

    return settle(outcome);
  }

  listThemes(): Promise<ThemeChoice[]> {
    return Promise.resolve(this.themes);
  }

  setTheme(_surveyId: string, themeId: string | null): Promise<void> {
    this.themeCalls.push(themeId);
    this.storedLook = { tokens: themePresets[0]!.tokens, own: false, lookBase: null, themeId };
    return Promise.resolve();
  }

  look(): Promise<SurveyLook<ThemeTokens>> {
    return Promise.resolve(this.storedLook);
  }

  setLook(_surveyId: string, tokens: ThemeTokens, lookBase: string | null): Promise<void> {
    this.lookCalls.push({ tokens, lookBase });
    this.storedLook = { tokens, own: true, lookBase, themeId: this.storedLook.themeId };
    return Promise.resolve();
  }

  saveTheme(name: string): Promise<string> {
    if (this.saveThemeOutcome !== "ok") {
      return Promise.reject(this.saveThemeOutcome);
    }

    this.savedThemes.push(name);
    return Promise.resolve(`t${this.savedThemes.length + 2}`);
  }

  uploadImage(file: File): Promise<string> {
    this.uploads.push(file.name);
    return Promise.resolve(`a${this.uploads.length}`);
  }

  saveAsTemplate(_surveyId: string, name: string): Promise<void> {
    this.templates.push(name);
    return Promise.resolve();
  }

  assetUrl(assetId: string, width?: number): string {
    return `/api/app/assets/${assetId}${width === undefined ? "" : `?w=${width}`}`;
  }

  setDraft(draft: Outcome<SurveyDraft>) {
    this.draft = draft;
  }
}

function settle<T>(outcome: Outcome<T>): Promise<T> {
  if (outcome === "pending") {
    return new Promise<T>(() => undefined);
  }

  return outcome instanceof ApiError ? Promise.reject(outcome) : Promise.resolve(outcome);
}

/** Sequential ids for new questions and pages, so tests are deterministic. */
export function sequentialIds(prefix = "new") {
  let next = 0;

  return () => {
    next += 1;
    return `${prefix}${next}`;
  };
}
