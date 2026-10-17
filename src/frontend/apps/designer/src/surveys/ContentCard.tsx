// SPDX-License-Identifier: AGPL-3.0-only
import type { KeyResult, SurveyFront } from "@askwerk/app-client";
import { createTranslator, themePresets, type Locale, type ThemeTokens } from "@askwerk/schema";
import { useState, type ReactNode } from "react";
import { frontAssetUrl, LookStrip } from "../designer/look/FrontCard";
import { surveysMessages } from "./messages";

/** Question types as a card names them (W32): choices together, texts together. */
type Kind = "kindChoice" | "kindYesNo" | "kindSlider" | "kindRating" | "kindNps" | "kindText" | "kindNumber" | "kindDate" | "kindMatrix" | "kindRanking";
const kinds: Record<string, Kind> = {
  single: "kindChoice",
  multiple: "kindChoice",
  dropdown: "kindChoice",
  "yes-no": "kindYesNo",
  slider: "kindSlider",
  rating: "kindRating",
  nps: "kindNps",
  "short-text": "kindText",
  "long-text": "kindText",
  number: "kindNumber",
  date: "kindDate",
  matrix: "kindMatrix",
  ranking: "kindRanking",
};

/** A text in the reader's language, else the survey's default, else any. */
export function frontText(text: Record<string, string> | null | undefined, locale: string, fallback: string): string {
  return text == null ? "" : (text[locale] ?? text[fallback] ?? Object.values(text)[0] ?? "").trim();
}

/** "2 hours ago", "yesterday", in the reader's language. */
function ago(locale: Locale, at: string, now: Date): string {
  const minutes = Math.round((Date.parse(at) - now.getTime()) / 60_000);
  const format = new Intl.RelativeTimeFormat(locale, { numeric: "auto" });

  return Math.abs(minutes) < 60 ? format.format(minutes, "minute") : Math.abs(minutes) < 1440 ? format.format(Math.round(minutes / 60), "hour") : format.format(Math.round(minutes / 1440), "day");
}

/** The name of a card's look (W32): one of the organisation's by its name, an Askwerk look in the reader's language. */
export function lookNameOf(front: SurveyFront, locale: string): string {
  const preset = front.lookPreset == null ? undefined : themePresets.find((each) => each.id === front.lookPreset);

  return front.lookName ?? (preset === undefined ? "" : (preset.name[locale] ?? preset.name.en ?? preset.id));
}

type ContentCardProps = {
  front: SurveyFront;
  /** The internal name: the title when the welcome page has none. */
  name: string;
  locale: Locale;
  organisation: string;
  onOpen?: () => void;
  openLabel?: string;
  /** Status, dates, code, languages, actions. */
  footer: ReactNode;
  now?: Date;
  /** What the card shows (W38): what the survey asks (A) or its key result (E). */
  show?: "contents" | "key";
};

/**
 * A card that says what a survey is (W32 card A): the look as a strip, then its purpose, size and time, the first
 * questions with their types, how answering goes, and its facts. Templates and samples have no answers.
 */
export function ContentCard({ front, name, locale, organisation, onOpen, openLabel, footer, now = new Date(), show = "contents" }: ContentCardProps) {
  const [shown, setShown] = useState(0);
  const t = createTranslator(surveysMessages, locale);
  const title = frontText(front.title, locale, front.defaultLanguage) || name;
  const lead = frontText(front.text, locale, front.defaultLanguage);
  const contents = front.contents ?? null;
  const answers = front.answers ?? null;
  const strip = <LookStrip tokens={front.look as ThemeTokens} organisation={organisation} start={t("start")} headerAsset={front.headerImage ?? null} assetUrl={frontAssetUrl} lookName={lookNameOf(front, locale)} />;
  const counted = new Map<string, number>();

  for (const [type, count] of Object.entries(contents?.types ?? {})) {
    const kind = kinds[type];
    kind !== undefined && counted.set(kind, (counted.get(kind) ?? 0) + count);
  }

  // The four most common types; the rest are in the survey.
  const typeText = [...counted].sort((a, b) => b[1] - a[1]).slice(0, 4).map(([kind, count]) => `${count} ${count === 1 ? t(kind as Kind) : t(`${kind as Kind}s`)}`).join(" · ");
  const total = answers === null ? 0 : answers.complete + answers.partial;
  const highest = Math.max(1, ...(answers?.days ?? []));

  return (
    <li className="content-card">
      {onOpen === undefined ? (
        strip
      ) : (
        <button type="button" className="content-card__strip" aria-label={openLabel} onClick={onOpen}>
          {strip}
        </button>
      )}
      <div className="content-card__what">
        <h2>{title}</h2>
        {lead !== "" && <p className="content-card__lead">{lead}</p>}
        {show === "key" && contents !== null && (
          <p className="content-card__size">
            <span>{contents.questions === 1 ? t("sizeQuestionOne") : t("sizeQuestions", { count: contents.questions })}</span>
            <span>{contents.pages === 1 ? t("sizePageOne") : t("sizePages", { count: contents.pages })}</span>
            <span>{t("sizeMinutes", { minutes: contents.minutes })}</span>
          </p>
        )}
        {show === "contents" && contents !== null && (
          <>
            <p className="content-card__size">
              <span>{contents.questions === 1 ? t("sizeQuestionOne") : t("sizeQuestions", { count: contents.questions })}</span>
              <span>{contents.pages === 1 ? t("sizePageOne") : t("sizePages", { count: contents.pages })}</span>
              <span>{t("sizeMinutes", { minutes: contents.minutes })}</span>
            </p>
            {contents.first.length > 0 && (
              <ol className="content-card__questions">
                {contents.first.map((question, index) => (
                  <li key={index}>
                    <span aria-hidden="true">{String(index + 1).padStart(2, "0")}</span>
                    <span>{frontText(question.text, locale, front.defaultLanguage)}</span>
                    <span className="content-card__type">{kinds[question.type] === undefined ? question.type : t(kinds[question.type]!)}</span>
                  </li>
                ))}
                {contents.questions > contents.first.length && <li className="content-card__more">{t("moreQuestions", { count: contents.questions - contents.first.length, types: typeText })}</li>}
              </ol>
            )}
          </>
        )}
      </div>
      {show === "key" && (
        <KeyResultBox
          t={t}
          locale={locale}
          front={front}
          keys={front.keys ?? []}
          shown={Math.min(shown, Math.max(0, (front.keys?.length ?? 1) - 1))}
          onShow={setShown}
          last={answers?.lastAt != null ? (Math.abs(Date.parse(answers.lastAt) - now.getTime()) < 60_000 ? t("justNow") : t("lastAnswer", { when: ago(locale, answers.lastAt, now) })) : null}
          responses={total}
          complete={total === 0 || answers === null ? 0 : Math.round((answers.complete / total) * 100)}
        />
      )}
      {show === "key" ? null : answers !== null ? (
        <div className="content-card__answers">
          <span className="content-card__count">
            {total}
            <small>{total === 1 ? t("responseWord") : t("responsesWord")}</small>
          </span>
          <span className="content-card__spark" aria-hidden="true">
            {answers.days.map((day, index) => (
              <i key={index} style={{ height: `${Math.max(2, Math.round((day / highest) * 28))}px` }} />
            ))}
          </span>
          <span className="content-card__sub">
            {t("completeShare", { share: total === 0 ? 0 : Math.round((answers.complete / total) * 100) })}
            {answers.lastAt != null && ` · ${Math.abs(Date.parse(answers.lastAt) - now.getTime()) < 60_000 ? t("justNow") : t("lastAnswer", { when: ago(locale, answers.lastAt, now) })}`}
          </span>
        </div>
      ) : (
        front.code != null && <div className="content-card__answers content-card__answers--none">{front.publishedAt == null ? t("noAnswersPublish") : t("noAnswersYet")}</div>
      )}
      <div className="content-card__foot">{footer}</div>
    </li>
  );
}

const npsColours = ["#c94a3a", "#d9d2c3", "#2f6f4f"];
const scaleColours = ["#c94a3a", "#e39b58", "#d9d2c3", "#7fb08f", "#2f6f4f"];

/** A colour for a value of a scale: red at the bottom, green at the top. */
function scaleColour(index: number, count: number): string {
  return scaleColours[count <= 1 ? 4 : Math.round((index / (count - 1)) * 4)]!;
}

/**
 * The key result (W38 card E): the key question's main answer (NPS, average, the top choice's share) with its spread.
 * Several key questions: ‹ 1 / 2 › steps through them. A draft says what will show.
 */
function KeyResultBox({
  t,
  locale,
  front,
  keys,
  shown,
  onShow,
  last,
  responses,
  complete,
}: {
  t: ReturnType<typeof createTranslator<keyof (typeof surveysMessages)["en"]>>;
  locale: Locale;
  front: SurveyFront;
  keys: KeyResult[];
  shown: number;
  onShow: (index: number) => void;
  last: string | null;
  /** All responses and the share complete: the legend when everyone answered the key question. */
  responses: number;
  complete: number;
}) {
  const key = keys[shown];

  if (key === undefined) {
    return <div className="content-card__key content-card__key--none">{t("noKeyQuestion")}</div>;
  }

  const text = frontText(key.text, locale, front.defaultLanguage);
  const total = key.spread.reduce((sum, slice) => sum + slice.count, 0);
  const sign = key.type === "nps" && key.value !== null && key.value > 0 ? "+" : "";
  const value = key.value === null ? null : key.type === "rating" || key.type === "slider" ? key.value.toLocaleString(locale, { maximumFractionDigits: 1 }) : `${sign}${key.value}`;
  const big = key.top !== null ? frontText(key.top, locale, front.defaultLanguage) : value;
  const unit = key.top !== null ? `${key.value} %` : key.type === "nps" ? "NPS" : key.max !== null ? t("ofMax", { max: key.max }) : "";

  return (
    <div className="content-card__key">
      <span className="content-card__key-top">
        {!key.auto && (
          <span className="designer-key" aria-label={t("keyQuestion")} role="img">
            ★
          </span>
        )}
        <span className="content-card__key-question">{text}</span>
        {/* Picked automatically (W38): said at the line's end, where the pager would be. */}
        {key.auto && <span className="content-card__auto">{t(key.type === "nps" || key.type === "rating" ? "autoRating" : "autoChoice")}</span>}
        {keys.length > 1 && (
          <span className="content-card__pager">
            <button type="button" aria-label={t("previousKey")} disabled={shown === 0} onClick={() => onShow(shown - 1)}>
              ‹
            </button>
            <span>
              {shown + 1} / {keys.length}
            </span>
            <button type="button" aria-label={t("nextKey")} disabled={shown === keys.length - 1} onClick={() => onShow(shown + 1)}>
              ›
            </button>
          </span>
        )}
      </span>
      {front.publishedAt == null ? (
        <span className="content-card__key-note">{t("keyNotPublished")}</span>
      ) : key.value === null ? (
        <span className="content-card__key-note">{t("noAnswersYet")}</span>
      ) : (
        <>
          <span className="content-card__key-value">
            {big}
            <small>{unit}</small>
          </span>
          <span className="content-card__dist" aria-hidden="true">
            {key.spread.map((slice, index) => (
              <i
                key={slice.code}
                style={{
                  width: `${total === 0 ? 0 : (slice.count / total) * 100}%`,
                  background: key.type === "nps" ? npsColours[index] : key.top === null ? scaleColour(index, key.spread.length) : scaleColours[Math.max(0, 4 - index)],
                }}
              />
            ))}
          </span>
          <span className="content-card__legend">
            <span>
              {key.answered === responses
                ? responses === 1
                  ? t("responseCompleteOne", { share: complete })
                  : t("responsesComplete", { count: responses, share: complete })
                : key.answered === 1
                  ? t("answerOne")
                  : t("answersCount", { count: key.answered })}
            </span>
            {last !== null && <span>{last}</span>}
          </span>
        </>
      )}
    </div>
  );
}
