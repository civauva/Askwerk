// SPDX-License-Identifier: AGPL-3.0-only
import type { AnswerValue } from "@askwerk/app-client";
import type { Question, SurveyDefinition, Translate, TranslatedText } from "@askwerk/schema";
import { ComboBox, DatePicker, TimeField } from "@askwerk/ui";
import { createContext, useContext, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from "react";
import type { RunnerMessageKey } from "./messages";

export type FieldProps = {
  t: Translate<RunnerMessageKey>;
  definition: SurveyDefinition;
  language: string;
  question: Question;
  value: AnswerValue | undefined;
  onChange: (value: AnswerValue | undefined) => void;
  /** The message to show at the question, if any (W8 B). */
  error?: string | undefined;
  onKeyDown?: (event: KeyboardEvent) => void;
  big?: boolean;
};

/** Where the page loads a file of this survey; a width asks for a smaller copy (AWK-17). */
export const AssetUrl = createContext<(assetId: string, width?: number) => string>((assetId) => assetId);

/** Image sizes for srcset: phones get the small copy, wide screens the larger ones. */
const srcSet = (url: (assetId: string, width?: number) => string, asset: string) => [480, 960, 1600].map((width) => `${url(asset, width)} ${width}w`).join(", ");

export function textOf(text: TranslatedText | undefined, language: string, definition: SurveyDefinition): string {
  return text?.[language]?.trim() || text?.[definition.defaultLanguage] || "";
}

/** Colour emoji faces, from very unhappy to very happy; all five from the emoji block, so none falls back to a plain outline. */
const faces = ["\u{1F61E}", "\u{1F641}", "\u{1F610}", "\u{1F642}", "\u{1F600}"];

/** Number key → nth choice; Y/N (English) or J/N (German) for yes/no (W8 C). */
function choiceForKey(question: Question, key: string, language: string): string | number | undefined {
  const lower = key.toLowerCase();

  if (question.type === "yes-no") {
    const yesKey = language === "de" ? "j" : "y";
    return lower === yesKey ? (question.yes?.code ?? "1") : lower === "n" ? (question.no?.code ?? "0") : undefined;
  }

  const index = Number(key) - 1;

  if (!Number.isInteger(index) || index < 0 || index > 8) {
    return undefined;
  }

  if (question.type === "rating" || question.type === "nps") {
    const min = question.type === "nps" ? 0 : (question.scale?.min ?? 1);
    const max = question.type === "nps" ? 10 : (question.scale?.max ?? 5);
    return min + index <= max ? min + index : undefined;
  }

  if (question.type === "single") {
    return question.options?.[index]?.code;
  }

  return undefined;
}

/** One question as respondents see it (W8 A, D, G; M4): a group named by its text, the answer, and its error. */
export function QuestionField(props: FieldProps) {
  const { t, definition, language, question, value, onChange, error, onKeyDown, big } = props;
  const id = `q-${question.id}`;
  const text = textOf(question.text, language, definition);
  const help = textOf(question.help, language, definition);

  if (question.type === "image" && question.image !== undefined) {
    return <ImageBlock {...props} />;
  }

  if (question.type === "text-block") {
    return (
      <section className={`run-q run-q--block${big ? " run-q--big" : ""}`} id={id} tabIndex={-1} onKeyDown={onKeyDown}>
        <p className="run-q__block">{text}</p>
      </section>
    );
  }

  function onGroupKeyDown(event: KeyboardEvent) {
    const target = event.target as HTMLElement;
    const typing = target.tagName === "TEXTAREA" || (target.tagName === "INPUT" && !["radio", "checkbox"].includes((target as HTMLInputElement).type));

    if (!typing && !event.ctrlKey && !event.metaKey && !event.altKey) {
      const choice = choiceForKey(question, event.key, language);

      if (choice !== undefined) {
        event.preventDefault();
        onChange(typeof choice === "number" ? { number: choice } : { option: choice });
        return;
      }
    }

    onKeyDown?.(event);
  }

  return (
    <fieldset className={`run-q${error ? " run-q--bad" : ""}${big ? " run-q--big" : ""}`} id={id} tabIndex={-1} aria-describedby={error ? `${id}-error` : undefined} onKeyDown={onGroupKeyDown}>
      <legend id={`${id}-legend`} className="run-q__title">
        {text}
        {question.required && (
          <span className="run-q__req" aria-hidden="true">
            {" "}
            *
          </span>
        )}
      </legend>
      {help && <p className="run-q__help">{help}</p>}
      {question.image !== undefined && <QuestionImage image={question.image} alt={textOf(question.image.alt, language, definition)} />}
      {error && (
        <p id={`${id}-error`} className="run-q__error">
          {error}
        </p>
      )}
      <Answer {...props} />
      {question.dontKnow === true && (
        <label className="run-choice run-choice--quiet">
          <input type="checkbox" checked={value?.dontKnow === true} onChange={(event) => onChange(event.target.checked ? { dontKnow: true } : undefined)} />
          <span>{t("dontKnow")}</span>
        </label>
      )}
    </fieldset>
  );
}

function Answer({ t, definition, language, question, value, onChange }: FieldProps) {
  const name = `answer-${question.id}`;
  const labelOf = (label: TranslatedText) => textOf(label, language, definition);
  const otherField = (chosen: boolean) =>
    question.other !== undefined && (
      <input
        className="run-input run-choice__other"
        aria-label={labelOf(question.other.label)}
        value={value?.other ?? ""}
        tabIndex={chosen ? 0 : -1}
        onChange={(event) =>
          onChange(
            question.type === "multiple"
              ? { options: Array.from(new Set([...(value?.options ?? []), question.other!.code])), other: event.target.value }
              : { option: question.other!.code, other: event.target.value },
          )
        }
      />
    );

  switch (question.type) {
    case "rating":
    case "nps": {
      const min = question.type === "nps" ? 0 : (question.scale?.min ?? 1);
      const max = question.type === "nps" ? 10 : (question.scale?.max ?? 5);
      const steps = Array.from({ length: Math.max(0, max - min + 1) }, (_, index) => min + index);
      const display = question.type === "rating" ? (question.display ?? "numbers") : "numbers";
      const chosen = value?.number;
      const symbol = (step: number, index: number): ReactNode => {
        switch (display) {
          case "stars":
            return chosen !== undefined && step <= chosen ? "★" : "☆";
          case "dots":
            return <i className="run-dot" style={{ width: 12 + index * 3, height: 12 + index * 3 }} />;
          case "faces":
            return <span className="run-face">{steps.length === 3 ? [faces[0], faces[2], faces[4]][index] : faces[index]}</span>;
          default:
            return step;
        }
      };

      return (
        <>
          <div className={`run-scale run-scale--${display}`} style={{ gridTemplateColumns: `repeat(${steps.length}, minmax(0, 1fr))` }}>
            {steps.map((step, index) => (
              <label key={step} className={chosen === step ? "run-scale__step run-scale__step--on" : "run-scale__step"}>
                <input
                  type="radio"
                  className="run-visually-hidden"
                  name={name}
                  checked={chosen === step}
                  onChange={() => onChange({ number: step })}
                />
                <span className="run-visually-hidden">{step}</span>
                <span aria-hidden="true">{symbol(step, index)}</span>
              </label>
            ))}
          </div>
          {(question.scale?.minLabel || question.scale?.maxLabel) && (
            <div className="run-scale__ends" aria-hidden="true">
              <span>{labelOf(question.scale?.minLabel ?? {})}</span>
              <span>{labelOf(question.scale?.maxLabel ?? {})}</span>
            </div>
          )}
        </>
      );
    }
    case "slider": {
      const scale = question.scale ?? { min: 0, max: 100, step: 1 };
      const answered = value?.number !== undefined;
      // The track fills up to the answer only; untouched it shows no fill, so it never looks half answered.
      const fill = answered ? Math.round((((value?.number ?? scale.min) - scale.min) / (scale.max - scale.min || 1)) * 100) : 0;
      const minLabel = labelOf(scale.minLabel ?? {});
      const maxLabel = labelOf(scale.maxLabel ?? {});

      return (
        <div className={answered ? "run-slider" : "run-slider run-slider--untouched"}>
          <output className="run-slider__value" aria-hidden="true">
            {answered && question.scale?.showValue !== false ? value?.number : answered ? "" : t("notAnswered")}
          </output>
          <input
            type="range"
            min={scale.min}
            max={scale.max}
            step={scale.step ?? 1}
            value={value?.number ?? Math.round((scale.min + scale.max) / 2)}
            aria-labelledby={`q-${question.id}-legend`}
            aria-valuetext={answered ? String(value?.number) : t("notAnswered")}
            style={{ "--fill": `${fill}%` } as CSSProperties}
            onChange={(event) => onChange({ number: Number(event.target.value) })}
            onPointerDown={(event) => !answered && onChange({ number: Number((event.target as HTMLInputElement).value) })}
          />
          <div className="run-scale__ends" aria-hidden="true">
            <span>{minLabel || scale.min}</span>
            <span>{maxLabel || scale.max}</span>
          </div>
        </div>
      );
    }
    case "yes-no": {
      const answers = [
        [question.yes?.code ?? "1", question.yes?.label ? labelOf(question.yes.label) : t("yes"), language === "de" ? "J" : "Y"],
        [question.no?.code ?? "0", question.no?.label ? labelOf(question.no.label) : t("no"), "N"],
      ] as const;

      return (
        <div className="run-yesno">
          {answers.map(([code, label, key]) => (
            <label key={code} className={value?.option === code ? "run-yesno__answer run-yesno__answer--on" : "run-yesno__answer"}>
              <input
                type="radio"
                className="run-visually-hidden"
                name={name}
                checked={value?.option === code}
                onChange={() => onChange({ option: code })}
              />
              <span>{label}</span>
              <kbd aria-hidden="true">{key}</kbd>
            </label>
          ))}
        </div>
      );
    }
    case "single":
      return (
        <div className="run-choices">
          {question.options?.map((option, index) => (
            <label key={option.code} className={value?.option === option.code ? "run-choice run-choice--on" : "run-choice"}>
              <input
                type="radio"
                name={name}
                checked={value?.option === option.code}
                onChange={() => onChange({ option: option.code })}
              />
              <Picture asset={option.image?.asset} />
              <span>{labelOf(option.label)}</span>
              {index < 9 && (
                <kbd aria-hidden="true" className="run-choice__key">
                  {index + 1}
                </kbd>
              )}
            </label>
          ))}
          {question.other !== undefined && (
            <label className={value?.option === question.other.code ? "run-choice run-choice--on" : "run-choice"}>
              <input type="radio" name={name} checked={value?.option === question.other.code} onChange={() => onChange({ option: question.other!.code, other: value?.other ?? "" })} />
              <span>{labelOf(question.other.label)}</span>
              {otherField(value?.option === question.other.code)}
            </label>
          )}
        </div>
      );
    case "multiple": {
      const chosen = value?.options ?? [];
      const toggle = (code: string, on: boolean) => {
        const next = on ? [...chosen, code] : chosen.filter((each) => each !== code);
        const keepOther = next.includes(question.other?.code ?? "") ? { other: value?.other ?? "" } : {};
        onChange(next.length === 0 ? undefined : { options: next, ...keepOther });
      };

      return (
        <div className="run-choices">
          {question.options?.map((option) => (
            <label key={option.code} className={chosen.includes(option.code) ? "run-choice run-choice--on" : "run-choice"}>
              <input type="checkbox" checked={chosen.includes(option.code)} onChange={(event) => toggle(option.code, event.target.checked)} />
              <Picture asset={option.image?.asset} />
              <span>{labelOf(option.label)}</span>
            </label>
          ))}
          {question.other !== undefined && (
            <div className={chosen.includes(question.other.code) ? "run-choice run-choice--on" : "run-choice"}>
              <label className="run-choice__label">
                <input type="checkbox" checked={chosen.includes(question.other.code)} onChange={(event) => toggle(question.other!.code, event.target.checked)} />
                <span>{labelOf(question.other.label)}</span>
              </label>
              {otherField(chosen.includes(question.other.code))}
            </div>
          )}
        </div>
      );
    }
    case "dropdown": {
      const options = [...(question.options ?? []).map((option) => ({ value: option.code, label: labelOf(option.label) }))];
      const sorted = question.order === "alphabetical" ? options.sort((a, b) => a.label.localeCompare(b.label, language)) : options;

      return (
        <>
          <ComboBox
            labelledBy={`q-${question.id}-legend`}
            options={question.other ? [...sorted, { value: question.other.code, label: labelOf(question.other.label) }] : sorted}
            value={value?.option ?? null}
            onChange={(option) => onChange({ option, ...(option === question.other?.code && { other: value?.other ?? "" }) })}
            texts={{ choose: t("choose"), search: t("search"), results: (shown, total) => t("results", { shown, total }), noResults: t("noResults") }}
          />
          {question.other !== undefined && value?.option === question.other.code && otherField(true)}
        </>
      );
    }
    case "short-text":
    case "number": {
      const type = question.format === "email" ? "email" : question.format === "phone" ? "tel" : question.format === "url" ? "url" : "text";

      return (
        <input
          className="run-input"
          type={type}
          inputMode={question.type === "number" ? "decimal" : type === "text" ? undefined : type === "tel" ? "tel" : type}
          autoComplete={question.format === "email" ? "email" : question.format === "phone" ? "tel" : "off"}
          aria-labelledby={`q-${question.id}-legend`}
          placeholder={textOf(question.placeholder, language, definition) || undefined}
          value={question.type === "number" ? (value?.number ?? "") : (value?.text ?? "")}
          onChange={(event) => {
            const raw = event.target.value;

            if (question.type === "number") {
              const parsed = Number(raw.replace(",", "."));
              onChange(raw.trim() === "" || Number.isNaN(parsed) ? undefined : { number: parsed });
            } else {
              onChange(raw === "" ? undefined : { text: raw });
            }
          }}
        />
      );
    }
    case "long-text":
      return (
        <>
          <textarea
            className="run-input run-input--long"
            aria-labelledby={`q-${question.id}-legend`}
            maxLength={2000}
            placeholder={t("yourAnswer")}
            value={value?.text ?? ""}
            onChange={(event) => onChange(event.target.value === "" ? undefined : { text: event.target.value })}
          />
          <span className="run-count" aria-hidden="true">
            {t("characters", { count: (value?.text ?? "").length, max: 2000 })}
          </span>
        </>
      );
    case "date":
      return <DateAnswer t={t} definition={definition} language={language} question={question} value={value} onChange={onChange} />;
    case "matrix":
      return (
        <table className="run-matrix">
          <thead>
            <tr>
              <td />
              {question.options?.map((option) => (
                <th key={option.code} scope="col">
                  {labelOf(option.label)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {question.rows?.map((row) => (
              <tr key={row.code}>
                <th scope="row">{labelOf(row.label)}</th>
                {question.options?.map((option) => (
                  <td key={option.code} data-label={labelOf(option.label)}>
                    <input
                      type="radio"
                      name={`${name}-${row.code}`}
                      aria-label={`${labelOf(row.label)}: ${labelOf(option.label)}`}
                      checked={value?.rows?.[row.code] === option.code}
                      onChange={() => onChange({ rows: { ...value?.rows, [row.code]: option.code } })}
                    />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      );
    case "ranking":
      return <RankingAnswer t={t} definition={definition} language={language} question={question} value={value} onChange={onChange} />;
    default:
      return null;
  }
}

function DateAnswer({ t, language, question, value, onChange }: FieldProps) {
  const mode = question.mode ?? "date";
  const [datePart, timePart] = mode === "date-time" ? (value?.date?.split("T") ?? [undefined, undefined]) : [undefined, undefined];
  const dateTexts = { openCalendar: t("chooseDate"), previousMonth: t("previousMonth"), nextMonth: t("nextMonth"), invalid: t("dateInvalid") };
  const timeTexts = { chooseTime: t("chooseTime"), invalid: t("timeInvalid") };
  const id = `q-${question.id}-answer`;
  const today = new Date().toISOString().slice(0, 10);
  const limit = (bound: string | undefined) => (bound === "today" ? today : bound);
  const set = (date: string | null | undefined) => onChange(date ? { date } : undefined);

  if (mode === "time") {
    return (
      <span className="run-date">
        <label className="run-visually-hidden" htmlFor={id}>
          {t("time")}
        </label>
        <TimeField id={id} value={value?.date ?? null} minuteStep={question.minuteStep ?? 15} texts={timeTexts} onChange={set} />
      </span>
    );
  }

  if (mode === "date-time") {
    const join = (date: string | null | undefined, time: string | null | undefined) => (date && time ? `${date}T${time}` : date ? `${date}T${time ?? ""}` : undefined);

    return (
      <span className="run-date">
        <label className="run-visually-hidden" htmlFor={`${id}-date`}>
          {t("date")}
        </label>
        <DatePicker id={`${id}-date`} locale={language} value={datePart ?? null} texts={dateTexts} min={limit(question.earliest)?.slice(0, 10)} max={limit(question.latest)?.slice(0, 10)} onChange={(date) => set(join(date, timePart))} />
        <label className="run-visually-hidden" htmlFor={`${id}-time`}>
          {t("time")}
        </label>
        <TimeField id={`${id}-time`} value={timePart || null} minuteStep={question.minuteStep ?? 15} texts={timeTexts} onChange={(time) => set(join(datePart, time))} />
      </span>
    );
  }

  return (
    <span className="run-date">
      <label className="run-visually-hidden" htmlFor={id}>
        {t("date")}
      </label>
      <DatePicker id={id} locale={language} value={value?.date ?? null} texts={dateTexts} min={limit(question.earliest)} max={limit(question.latest)} onChange={set} />
    </span>
  );
}

function RankingAnswer({ t, definition, language, question, value, onChange }: FieldProps) {
  const codes = (question.options ?? []).map((option) => option.code);
  const order = value?.order ?? codes;
  const labelOf = (code: string) => textOf(question.options?.find((option) => option.code === code)?.label, language, definition);
  const move = (index: number, by: number) => {
    const next = [...order];
    const [item] = next.splice(index, 1);
    next.splice(index + by, 0, item!);
    onChange({ order: next });
  };

  return (
    <ol className="run-ranking">
      {order.map((code, index) => (
        <li key={code} className="run-choice">
          <span className="run-ranking__place" aria-hidden="true">
            {index + 1}
          </span>
          <span className="run-ranking__label">{labelOf(code)}</span>
          <button type="button" className="run-icon-button" aria-label={t("moveUp", { item: labelOf(code) })} disabled={index === 0} onClick={() => move(index, -1)}>
            ↑
          </button>
          <button type="button" className="run-icon-button" aria-label={t("moveDown", { item: labelOf(code) })} disabled={index === order.length - 1} onClick={() => move(index, 1)}>
            ↓
          </button>
        </li>
      ))}
    </ol>
  );
}

/** An image block (AWK-17, W10 F1): the image with its alt text, an optional caption, size and alignment. */
function ImageBlock({ question, language, definition, big }: FieldProps) {
  const url = useContext(AssetUrl);
  const image = question.image!;
  const caption = textOf(question.text, language, definition);

  return (
    <section className={`run-q run-q--block${big ? " run-q--big" : ""}`} id={`q-${question.id}`} tabIndex={-1}>
      <figure className={`run-figure run-figure--${image.size ?? "full"} run-figure--${image.align ?? "centre"}`}>
        <img src={url(image.asset, 960)} srcSet={srcSet(url, image.asset)} sizes="(max-width: 700px) 100vw, 640px" alt={textOf(image.alt, language, definition)} />
        {caption && <figcaption>{caption}</figcaption>}
      </figure>
    </section>
  );
}

/** The image that goes with a question. */
function QuestionImage({ image, alt }: { image: { readonly asset: string }; alt: string }) {
  const url = useContext(AssetUrl);

  return <img className="run-q__image" src={url(image.asset, 960)} srcSet={srcSet(url, image.asset)} sizes="(max-width: 700px) 100vw, 640px" alt={alt} />;
}

/** An option's picture; the option's label names it, so the image itself is decorative. */
function Picture({ asset }: { asset: string | undefined }) {
  const url = useContext(AssetUrl);

  return asset === undefined ? null : <img className="run-choice__picture" src={url(asset, 480)} alt="" />;
}
