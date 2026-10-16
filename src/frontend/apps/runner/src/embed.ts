// SPDX-License-Identifier: AGPL-3.0-only
import type { RunClient } from "@askwerk/app-client";

/** A message to the page that embeds the survey (docs/api/embed.md). */
export type EmbedEvent =
  | { type: "askwerk:started"; survey: string; language: string }
  | { type: "askwerk:page"; survey: string; page: string }
  | { type: "askwerk:completed"; survey: string }
  | { type: "askwerk:resize"; survey: string; height: number };

/**
 * The run client, telling the embedding page when the respondent starts, saves a page and finishes (AWK-7). Only
 * these facts go out, never answers; who may embed is limited by the organisation's list of websites.
 */
export function withEmbedEvents(client: RunClient, survey: string, post: (event: EmbedEvent) => void): RunClient {
  return {
    open: (surveyId) => client.open(surveyId),
    start: async (surveyId, language) => {
      const response = await client.start(surveyId, language);
      post({ type: "askwerk:started", survey, language });
      return response;
    },
    savePage: async (surveyId, pageId, answers) => {
      await client.savePage(surveyId, pageId, answers);
      post({ type: "askwerk:page", survey, page: pageId });
    },
    complete: async (surveyId) => {
      await client.complete(surveyId);
      post({ type: "askwerk:completed", survey });
    },
    assetUrl: (surveyId, assetId, width) => client.assetUrl(surveyId, assetId, width),
  };
}

/** Tells the embedding page the survey's height whenever it changes, so the frame never scrolls on its own. */
export function postHeight(survey: string, post: (event: EmbedEvent) => void): void {
  let last = 0;
  new ResizeObserver(() => {
    const height = Math.ceil(document.documentElement.scrollHeight);

    if (height !== last) {
      last = height;
      post({ type: "askwerk:resize", survey, height });
    }
  }).observe(document.body);
}
