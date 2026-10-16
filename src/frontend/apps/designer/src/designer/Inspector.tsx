// SPDX-License-Identifier: AGPL-3.0-only
import { createTranslator, variableNamePattern, type Question, type QuestionType } from "@askwerk/schema";
import { ComboBox, DatePicker, TimeField, Switch, Tabs, RadioGroup as SharedRadioGroup } from "@askwerk/ui";
import { useId, useRef, useState } from "react";
import { designerMessages } from "./messages";
import { ImageButton, useImages } from "./images";
import { LogicOverview, PageLogic, QuestionLogic } from "./LogicEditor";
import { pageName } from "./Outline";
import { findQuestion, type ItemList } from "./state";
import { languageName, type RegionProps } from "./text";
import { missingTexts, translationProgress } from "./translations";

type InspectorProps = RegionProps & { typeNames: Record<QuestionType, string>; locale: string };

type Tab = "question" | "translations";

/** Right region: settings of the selected question, and the translation status of the survey (M1, M2). */
export function Inspector({ t, typeNames, state, dispatch, locale }: InspectorProps) {
  const question = findQuestion(state.definition, state.selectedQuestionId);
  const [tab, setTab] = useState<Tab>("question");
  const id = useId();

  if (state.edge === "welcome") {
    return (
      <aside className="designer-inspector" aria-label={t("welcomePage")}>
        <div className="designer-inspector__panel">
          <h2 className="designer-inspector__heading">{t("welcomePage")}</h2>
          <Switch label={t("welcomeShow")} checked={state.definition.welcome?.enabled !== false} onChange={(enabled) => dispatch({ type: "setWelcomeEnabled", enabled })} />
          <p className="designer-note">{t("welcomeShowNote")}</p>
        </div>
      </aside>
    );
  }

  if (state.edge === "end") {
    return (
      <aside className="designer-inspector" aria-label={t("thankYouPage")}>
        <div className="designer-inspector__panel">
          <h2 className="designer-inspector__heading">{t("thankYouPage")}</h2>
          <p className="designer-note">{t("endNote")}</p>
        </div>
      </aside>
    );
  }

  if (state.edge === "rules") {
    return (
      <aside className="designer-inspector" aria-label={t("allRules")}>
        <div className="designer-inspector__panel">
          <h2 className="designer-inspector__heading">{t("allRules")}</h2>
          <LogicOverview t={t} state={state} dispatch={dispatch} />
        </div>
      </aside>
    );
  }

  const pageIndex = state.definition.pages.findIndex((page) => page.id === state.selectedPageId);

  if (pageIndex >= 0) {
    return <PageSettings t={t} state={state} dispatch={dispatch} pageIndex={pageIndex} />;
  }

  if (question === undefined) {
    return (
      <aside className="designer-inspector" aria-label={t("surveySettings")}>
        <p className="designer-note">{t("selectQuestion")}</p>
      </aside>
    );
  }

  const tabs: readonly (readonly [Tab, string])[] = [
    ["question", t("tabQuestion")],
    ["translations", t("tabTranslations")],
  ];

  return (
    <aside className="designer-inspector" aria-label={t("questionSettings")}>
      <div className="designer-tabs">
        <Tabs label={t("questionSettings")} tabs={tabs} value={tab} onChange={setTab} idPrefix={id} />
      </div>
      <div role="tabpanel" id={`${id}-panel-${tab}`} aria-labelledby={`${id}-tab-${tab}`} className="designer-inspector__panel">
        {tab === "question" ? (
          <QuestionSettings t={t} typeNames={typeNames} state={state} dispatch={dispatch} question={question} locale={locale} />
        ) : (
          <TranslationStatus t={t} state={state} dispatch={dispatch} />
        )}
      </div>
    </aside>
  );
}

function QuestionSettings({ t, typeNames, state, dispatch, question, locale }: InspectorProps & { question: Question }) {
  const variableValid = variableNamePattern.test(question.variable);

  return (
    <>
      <div className="designer-field">
        <span className="designer-label" id="inspector-type-label">
          {t("type")}
        </span>
        <ComboBox
          id="inspector-type"
          labelledBy="inspector-type-label"
          options={questionTypes.map((type) => ({ value: type, label: typeNames[type] }))}
          value={question.type}
          onChange={(type) => dispatch({ type: "setType", questionId: question.id, questionType: type as QuestionType })}
          texts={{ choose: t("type"), search: t("comboSearch"), results: (shown, total) => t("comboResults", { shown, total }), noResults: t("comboNoResults") }}
        />
      </div>
      <div className="designer-field">
        <label className="designer-label" htmlFor="inspector-variable">
          {t("variableName")}
        </label>
        <div className="designer-input designer-input--with-hint">
          <input
            id="inspector-variable"
            className="designer-mono"
            value={question.variable}
            aria-invalid={!variableValid}
            aria-describedby="inspector-variable-note"
            onChange={(event) => dispatch({ type: "setVariable", questionId: question.id, variable: event.target.value })}
          />
          <span className="designer-meta" aria-hidden="true">
            {t("variableRule")}
          </span>
        </div>
        <p id="inspector-variable-note" className={variableValid ? "designer-note" : "designer-note designer-note--error"}>
          {variableValid ? t("variableNote") : t("variableInvalid")}
        </p>
      </div>
      {question.type === "rating" && <ScaleSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.type === "slider" && <SliderSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.type === "dropdown" && <OrderSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.options !== undefined && <ItemSettings t={t} state={state} dispatch={dispatch} question={question} list="options" />}
      {otherTypes.includes(question.type) && <OtherSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.type === "yes-no" && <YesNoSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.type === "short-text" && <FormatSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.type === "date" && <DateSettings t={t} state={state} dispatch={dispatch} question={question} locale={locale} />}
      {question.rows !== undefined && <ItemSettings t={t} state={state} dispatch={dispatch} question={question} list="rows" />}
      {question.type === "image" && <ImageBlockSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.type !== "text-block" && question.type !== "image" && <QuestionImageSettings t={t} state={state} dispatch={dispatch} question={question} />}
      {question.type !== "text-block" && question.type !== "image" && (
        <Switch label={t("required")} checked={question.required} onChange={(required) => dispatch({ type: "setRequired", questionId: question.id, required })} />
      )}
      {question.options !== undefined && (
        <Switch
          label={t("randomize")}
          checked={question.randomize === true}
          onChange={(value) => dispatch({ type: "setFlag", questionId: question.id, flag: "randomize", value })}
        />
      )}
      {question.type !== "text-block" && question.type !== "image" && (
        <Switch
          label={t("dontKnow")}
          checked={question.dontKnow === true}
          onChange={(value) => dispatch({ type: "setFlag", questionId: question.id, flag: "dontKnow", value })}
        />
      )}
      <QuestionLogic t={t} state={state} dispatch={dispatch} question={question} />
    </>
  );
}

const questionTypes: readonly QuestionType[] = [
  "single",
  "multiple",
  "dropdown",
  "yes-no",
  "slider",
  "rating",
  "nps",
  "short-text",
  "long-text",
  "number",
  "date",
  "matrix",
  "ranking",
  "text-block",
  "image",
];

const scalePresets: readonly (readonly [number, number])[] = [
  [1, 5],
  [1, 7],
  [1, 10],
  [0, 10],
];

function ScaleSettings({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const min = question.scale?.min ?? 1;
  const max = question.scale?.max ?? 5;
  const isPreset = scalePresets.some(([low, high]) => low === min && high === max);
  const [custom, setCustom] = useState(!isPreset);
  const patch = (fields: Partial<Question>, remove?: readonly (keyof Question)[]) => dispatch({ type: "patch", questionId: question.id, patch: fields, ...(remove && { remove }) });

  return (
    <>
      <RadioGroup
        legend={t("display")}
        name={`display-${question.id}`}
        value={question.display ?? "numbers"}
        options={[
          ["numbers", t("displayNumbers")],
          ["stars", t("displayStars")],
          ["dots", t("displayDots")],
          ["faces", t("displayFaces")],
        ]}
        onChange={(value) => (value === "numbers" ? patch({}, ["display"]) : patch({ display: value as NonNullable<Question["display"]> }))}
      />
      <div className="designer-field">
        <span className="designer-label" id="inspector-scale-label">
          {t("scale")}
        </span>
        <ComboBox
          id="inspector-scale"
          labelledBy="inspector-scale-label"
          options={[
            ...scalePresets.map(([low, high]) => ({ value: `${low}:${high}`, label: t("scaleRange", { min: low, max: high }) })),
            { value: "custom", label: t("scaleCustom") },
          ]}
          value={custom ? "custom" : `${min}:${max}`}
          onChange={(value) => {
            if (value === "custom") {
              setCustom(true);
              return;
            }

            const [low, high] = value.split(":").map(Number);
            setCustom(false);
            dispatch({ type: "setScale", questionId: question.id, min: low ?? 1, max: high ?? 5 });
          }}
          texts={{ choose: t("scale") }}
        />
      </div>
      {custom && (
        <div className="designer-field designer-field--pair">
          <NumberField id="inspector-scale-min" label={t("min")} value={min} onChange={(value) => dispatch({ type: "setScale", questionId: question.id, min: value, max })} />
          <NumberField id="inspector-scale-max" label={t("max")} value={max} onChange={(value) => dispatch({ type: "setScale", questionId: question.id, min, max: value })} />
        </div>
      )}
      <ScaleLabels t={t} state={state} dispatch={dispatch} question={question} min={min} max={max} />
    </>
  );
}

function ScaleLabels({ t, state, dispatch, question, min, max }: RegionProps & { question: Question; min: number; max: number }) {
  const labelField = (end: "minLabel" | "maxLabel", value: number) => (
    <div className="designer-field">
      <label className="designer-label" htmlFor={`inspector-${end}`}>
        {t("labelAt", { value })}
      </label>
      <input
        id={`inspector-${end}`}
        className="designer-input designer-input--text"
        value={question.scale?.[end]?.[state.language] ?? ""}
        onChange={(event) => dispatch({ type: "setScaleLabel", questionId: question.id, end, language: state.language, value: event.target.value })}
      />
    </div>
  );

  return (
    <>
      {labelField("minLabel", min)}
      {labelField("maxLabel", max)}
    </>
  );
}

/** Slider (W7, panel 4): range, step, end labels, and whether the value shows above the handle. */
function SliderSettings({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const scale = question.scale ?? { min: 0, max: 100, step: 1 };
  const set = (fields: Partial<NonNullable<Question["scale"]>>) => dispatch({ type: "patch", questionId: question.id, patch: { scale: { ...scale, ...fields } } });

  return (
    <>
      <div className="designer-field designer-field--triple">
        <NumberField id="inspector-slider-min" label={t("min")} value={scale.min} onChange={(min) => set({ min })} />
        <NumberField id="inspector-slider-max" label={t("max")} value={scale.max} onChange={(max) => set({ max })} />
        <NumberField id="inspector-slider-step" label={t("step")} value={scale.step ?? 1} onChange={(step) => set({ step })} />
      </div>
      <ScaleLabels t={t} state={state} dispatch={dispatch} question={question} min={scale.min} max={scale.max} />
      <Switch label={t("showValue")} checked={scale.showValue === true} onChange={(showValue) => set({ showValue })} />
    </>
  );
}

/** Dropdown: the order the options are offered in. */
function OrderSettings({ t, dispatch, question }: RegionProps & { question: Question }) {
  return (
    <RadioGroup
      legend={t("order")}
      name={`order-${question.id}`}
      value={question.order ?? "entered"}
      options={[
        ["entered", t("orderEntered")],
        ["alphabetical", t("orderAlphabetical")],
      ]}
      onChange={(value) =>
        dispatch(value === "entered" ? { type: "patch", questionId: question.id, patch: {}, remove: ["order"] } : { type: "patch", questionId: question.id, patch: { order: "alphabetical" } })
      }
    />
  );
}

/** "Other, please specify" (W7 rev. 2, panel 2): an extra last option with its own code and text variable. */
function OtherSettings({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const other = question.other;
  const set = (fields: Partial<NonNullable<Question["other"]>>) =>
    other !== undefined && dispatch({ type: "patch", questionId: question.id, patch: { other: { ...other, ...fields } } });

  return (
    <>
      <Switch
        label={t("otherSwitch")}
        checked={other !== undefined}
        onChange={(on) =>
          dispatch(
            on
              ? {
                  type: "patch",
                  questionId: question.id,
                  patch: {
                    other: {
                      code: "OTHER",
                      label: Object.fromEntries(
                        state.definition.languages.filter((language) => language === "de" || language === "en").map((language) => [language, createTranslator(designerMessages, language)("otherDefault")]),
                      ),
                      variable: `${question.variable}_OTHER`.slice(0, 32),
                    },
                  },
                }
              : { type: "patch", questionId: question.id, patch: {}, remove: ["other"] },
          )
        }
      />
      {other !== undefined && (
        <>
          <TextField id="inspector-other-label" label={t("otherLabel")} value={other.label[state.language] ?? ""} onChange={(value) => set({ label: { ...other.label, [state.language]: value } })} />
          <div className="designer-field designer-field--pair">
            <TextField id="inspector-other-code" label={t("otherCode")} value={other.code} mono onChange={(code) => set({ code })} />
            <TextField id="inspector-other-variable" label={t("otherVariable")} value={other.variable} mono onChange={(variable) => set({ variable })} />
          </div>
        </>
      )}
    </>
  );
}

/** Yes/no (W7, panel 3): labels per language and codes; Yes/1 and No/0 when not set. */
function YesNoSettings({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const answer = (end: "yes" | "no") => {
    const current = question[end];
    const fallbackCode = end === "yes" ? "1" : "0";
    const set = (fields: Partial<NonNullable<Question["yes"]>>) =>
      dispatch({ type: "patch", questionId: question.id, patch: { [end]: { code: current?.code ?? fallbackCode, ...(current?.label && { label: current.label }), ...fields } } });

    return (
      <div className="designer-field designer-field--pair">
        <TextField
          id={`inspector-${end}-label`}
          label={end === "yes" ? t("labelOfYes") : t("labelOfNo")}
          value={current?.label?.[state.language] ?? ""}
          placeholder={end === "yes" ? t("yes") : t("no")}
          onChange={(value) => set({ label: { ...current?.label, [state.language]: value } })}
        />
        <TextField id={`inspector-${end}-code`} label={end === "yes" ? t("codeOfYes") : t("codeOfNo")} value={current?.code ?? fallbackCode} mono onChange={(code) => set({ code })} />
      </div>
    );
  };

  return (
    <>
      {answer("yes")}
      {answer("no")}
    </>
  );
}

/** Short text (W7, panel 5): the format it is checked against, and a placeholder. */
function FormatSettings({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const placeholder = question.placeholder;

  return (
    <>
      <RadioGroup
        legend={t("format")}
        name={`format-${question.id}`}
        value={question.format ?? "free"}
        options={[
          ["free", t("formatFree")],
          ["email", t("formatEmail")],
          ["phone", t("formatPhone")],
          ["url", t("formatUrl")],
        ]}
        onChange={(value) =>
          dispatch(
            value === "free"
              ? { type: "patch", questionId: question.id, patch: {}, remove: ["format"] }
              : { type: "patch", questionId: question.id, patch: { format: value as NonNullable<Question["format"]> } },
          )
        }
      />
      <TextField
        id="inspector-placeholder"
        label={t("placeholder")}
        value={placeholder?.[state.language] ?? ""}
        onChange={(value) => {
          const next: Record<string, string> = { ...placeholder, [state.language]: value };

          if (value === "") {
            delete next[state.language];
          }

          dispatch(
            Object.keys(next).length === 0
              ? { type: "patch", questionId: question.id, patch: {}, remove: ["placeholder"] }
              : { type: "patch", questionId: question.id, patch: { placeholder: next } },
          );
        }}
      />
    </>
  );
}

/** Date (W7, panel 6): date, date and time, or time only; earliest and latest with the shared pickers, and the minute step. */
function DateSettings({ t, dispatch, question, locale }: RegionProps & { question: Question; locale: string }) {
  const mode = question.mode ?? "date";
  const setValue = (key: "earliest" | "latest", value: string | null) =>
    dispatch(value === null ? { type: "patch", questionId: question.id, patch: {}, remove: [key] } : { type: "patch", questionId: question.id, patch: { [key]: value } });

  return (
    <>
      <RadioGroup
        legend={t("mode")}
        name={`mode-${question.id}`}
        value={mode}
        options={[
          ["date", t("modeDate")],
          ["date-time", t("modeDateTime")],
          ["time", t("modeTime")],
        ]}
        onChange={(value) =>
          dispatch(
            value === "date"
              ? { type: "patch", questionId: question.id, patch: {}, remove: ["mode", "minuteStep", "earliest", "latest"] }
              : { type: "patch", questionId: question.id, patch: { mode: value as NonNullable<Question["mode"]>, minuteStep: question.minuteStep ?? 15 }, remove: ["earliest", "latest"] },
          )
        }
      />
      <DateLimit t={t} end="earliest" mode={mode} value={question.earliest} minuteStep={question.minuteStep ?? 15} locale={locale} onChange={(value) => setValue("earliest", value)} />
      <DateLimit t={t} end="latest" mode={mode} value={question.latest} minuteStep={question.minuteStep ?? 15} locale={locale} onChange={(value) => setValue("latest", value)} />
      {mode !== "date" && (
        <NumberField
          id="inspector-minute-step"
          label={t("minuteStep")}
          value={question.minuteStep ?? 15}
          onChange={(minuteStep) => dispatch({ type: "patch", questionId: question.id, patch: { minuteStep } })}
        />
      )}
    </>
  );
}

/** Earliest or latest, written for the mode: "2026-10-14" or "today", "2026-10-14T09:30", "09:30" (AWK-19 pickers). */
function DateLimit({
  t,
  end,
  mode,
  value,
  minuteStep,
  locale,
  onChange,
}: {
  t: RegionProps["t"];
  end: "earliest" | "latest";
  mode: NonNullable<Question["mode"]>;
  value: string | undefined;
  minuteStep: number;
  locale: string;
  onChange: (value: string | null) => void;
}) {
  const label = t(end);
  const id = `inspector-${end}`;
  const dateTexts = { openCalendar: t("chooseDate"), previousMonth: t("previousMonth"), nextMonth: t("nextMonth"), invalid: t("dateInvalid") };
  const timeTexts = { chooseTime: t("chooseTime"), invalid: t("timeInvalid") };
  const [datePart, timePart] = mode === "date-time" && value !== undefined ? value.split("T") : [undefined, undefined];

  if (mode === "time") {
    return (
      <div className="designer-field">
        <label className="designer-label" htmlFor={id}>
          {label}
        </label>
        <TimeField id={id} value={value ?? null} minuteStep={minuteStep} texts={timeTexts} onChange={onChange} />
      </div>
    );
  }

  if (mode === "date-time") {
    const join = (date: string | null | undefined, time: string | null | undefined) => (date ? `${date}T${time ?? "00:00"}` : null);

    return (
      <div className="designer-date-limit">
        <div className="designer-field">
            <label className="designer-label" htmlFor={`${id}-date`}>
              {t(end === "earliest" ? "earliestDate" : "latestDate")}
            </label>
            <DatePicker id={`${id}-date`} locale={locale} value={datePart ?? null} texts={dateTexts} onChange={(date) => onChange(join(date, timePart))} />
          </div>
          <div className="designer-field">
            <label className="designer-label" htmlFor={`${id}-time`}>
              {t(end === "earliest" ? "earliestTime" : "latestTime")}
            </label>
            <TimeField id={`${id}-time`} value={timePart ?? null} minuteStep={minuteStep} texts={timeTexts} onChange={(time) => onChange(join(datePart, time ?? undefined))} />
        </div>
      </div>
    );
  }

  const today = value === "today";

  return (
    <div className="designer-field">
      <label className="designer-label" htmlFor={id}>
        {label}
      </label>
      {!today && <DatePicker id={id} locale={locale} value={value ?? null} texts={dateTexts} onChange={onChange} />}
      <label className="designer-radio">
        <input type="checkbox" checked={today} onChange={(event) => onChange(event.target.checked ? "today" : null)} />
        {t("today")}
      </label>
    </div>
  );
}

function RadioGroup({ legend, value, options, onChange }: { legend: string; name: string; value: string; options: readonly (readonly [string, string])[]; onChange: (value: string) => void }) {
  return <SharedRadioGroup legend={legend} value={value} options={options.map(([option, label]) => ({ value: option, label }))} onChange={onChange} />;
}

function NumberField({ id, label, value, onChange }: { id: string; label: string; value: number; onChange: (value: number) => void }) {
  return (
    <div className="designer-field">
      <label className="designer-label" htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        type="number"
        step={1}
        className="designer-input designer-input--text designer-mono"
        value={value}
        onChange={(event) => {
          const parsed = Number.parseInt(event.target.value, 10);

          if (!Number.isNaN(parsed)) {
            onChange(parsed);
          }
        }}
      />
    </div>
  );
}

function TextField({
  id,
  label,
  value,
  onChange,
  mono = false,
  placeholder,
  describedBy,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  mono?: boolean;
  placeholder?: string;
  describedBy?: string;
}) {
  return (
    <div className="designer-field">
      <label className="designer-label" htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        className={`designer-input designer-input--text${mono ? " designer-mono" : ""}`}
        value={value}
        placeholder={placeholder}
        aria-describedby={describedBy}
        onChange={(event) => onChange(event.target.value)}
      />
    </div>
  );
}

const otherTypes: readonly QuestionType[] = ["single", "multiple", "dropdown"];

function ItemSettings({ t, state, dispatch, question, list }: RegionProps & { question: Question; list: ItemList }) {
  const items = question[list] ?? [];
  const isRows = list === "rows";
  // Picture choices: single and multiple choice only (W10 F1).
  const pictures = !isRows && (question.type === "single" || question.type === "multiple");

  return (
    <fieldset className="designer-items">
      <legend className="designer-label">{isRows ? t("rowsGroup") : t("optionsGroup")}</legend>
      {items.map((item, index) => {
        const n = index + 1;

        return (
          <div key={index} className={pictures ? "designer-items__row designer-items__row--picture" : "designer-items__row"}>
            {pictures && <OptionPicture t={t} dispatch={dispatch} question={question} index={index} n={n} asset={item.image?.asset} />}
            <input
              className="designer-input designer-input--text"
              aria-label={isRows ? t("rowLabel", { n }) : t("optionLabel", { n })}
              value={item.label[state.language] ?? ""}
              placeholder={item.label[state.definition.defaultLanguage] ?? ""}
              onChange={(event) =>
                dispatch({ type: "setItemLabel", questionId: question.id, list, index, language: state.language, value: event.target.value })
              }
            />
            <input
              className="designer-input designer-input--text designer-mono designer-items__code"
              aria-label={isRows ? t("codeOfRow", { n }) : t("codeOfOption", { n })}
              value={item.code}
              onChange={(event) => dispatch({ type: "setItemCode", questionId: question.id, list, index, code: event.target.value })}
            />
            <button
              type="button"
              className="designer-items__remove"
              aria-label={isRows ? t("removeRow", { n }) : t("removeOption", { n })}
              onClick={() => dispatch({ type: "removeItem", questionId: question.id, list, index })}
            >
              ×
            </button>
          </div>
        );
      })}
      <button type="button" className="designer-outline__add" onClick={() => dispatch({ type: "addItem", questionId: question.id, list })}>
        <span aria-hidden="true">+ </span>
        {isRows ? t("addRow") : t("addOption")}
      </button>
    </fieldset>
  );
}

function TranslationStatus({ t, state, dispatch }: RegionProps) {
  const { definition, language } = state;
  const missing = missingTexts(definition);
  const progress = definition.languages.map((each) => ({ language: each, ...translationProgress(definition, each) }));
  const current = progress.find((each) => each.language === language);
  const summary = progress
    .map((each, index) =>
      `${t("progressItem", { language: languageName(t, each.language), present: each.present, total: each.total })}${index === 0 ? ` ${t("progressTexts")}` : ""}`,
    )
    .join(" · ");

  return (
    <>
      <div>
        <div className="designer-ruler" aria-hidden="true">
          <i style={{ width: `${current === undefined || current.total === 0 ? 100 : (current.present / current.total) * 100}%` }} />
        </div>
        <p className="designer-note">{summary}</p>
      </div>
      {definition.languages
        .filter((each) => missing.some((text) => text.language === each))
        .map((each) => (
          <div key={each}>
            <h3 className="designer-label" id={`missing-${each}`}>
              {t("missingIn", { language: languageName(t, each) })}
            </h3>
            <ul className="designer-missing" aria-labelledby={`missing-${each}`}>
              {missing
                .filter((text) => text.language === each)
                .map((text) => (
                  <li key={text.path}>
                    {text.questionId !== undefined ? (
                      <button type="button" className="designer-input" onClick={() => dispatch({ type: "select", questionId: text.questionId ?? null })}>
                        <span>{text.source}</span> <span className="designer-meta">{text.reference}</span>
                      </button>
                    ) : (
                      <span className="designer-input">
                        <span>{text.source}</span> <span className="designer-meta">{text.reference}</span>
                      </span>
                    )}
                  </li>
                ))}
            </ul>
          </div>
        ))}
      <p className="designer-note">{t("missingNote")}</p>
    </>
  );
}

/** The alt text of an image in the language being edited; required (W10 F1). */
function AltText({ t, state, value, onChange, id }: Pick<RegionProps, "t" | "state"> & { value: Question["text"] | undefined; onChange: (alt: Question["text"]) => void; id: string }) {
  const { language } = state;

  return (
    <div className="designer-field">
      <label className="designer-label" htmlFor={id}>
        {t("altText", { language: language.toUpperCase() })}
      </label>
      <input
        id={id}
        className="designer-input"
        value={value?.[language] ?? ""}
        onChange={(event) => {
          const next: Record<string, string> = { ...value, [language]: event.target.value };

          if (event.target.value === "") {
            delete next[language];
          }

          onChange(next);
        }}
      />
      <p className="designer-note">{t("altNote")}</p>
    </div>
  );
}

function ImageBlockSettings({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const image = question.image ?? { asset: "" };
  const patch = (change: Partial<NonNullable<Question["image"]>>) => dispatch({ type: "patch", questionId: question.id, patch: { image: { ...image, ...change } } });

  return (
    <>
      <AltText t={t} state={state} id="inspector-image-alt" value={image.alt} onChange={(alt) => patch({ alt })} />
      <RadioGroup
        legend={t("imageSize")}
        name="image-size"
        value={image.size ?? "full"}
        options={[
          ["full", t("sizeFull")],
          ["wide", t("sizeWide")],
          ["half", t("sizeHalf")],
          ["small", t("sizeSmall")],
        ]}
        onChange={(size) => patch({ size: size as "full" | "wide" | "half" | "small" })}
      />
      <RadioGroup
        legend={t("imageAlign")}
        name="image-align"
        value={image.align ?? "centre"}
        options={[
          ["left", t("alignLeft")],
          ["centre", t("alignCentre")],
          ["right", t("alignRight")],
        ]}
        onChange={(align) => patch({ align: align as "left" | "centre" | "right" })}
      />
    </>
  );
}

/** An image that goes with a question: upload, remove, alt text (W10 F1). */
function QuestionImageSettings({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const { url } = useImages();
  const image = question.image;

  return (
    <div className="designer-field">
      <span className="designer-label">{t("questionImage")}</span>
      {image !== undefined && <img className="designer-header-image" src={url(image.asset, 480)} alt="" />}
      <div className="designer-row">
        <ImageButton
          label={image === undefined ? t("uploadImage") : t("replaceImage")}
          fileLabel={t("questionImageFile")}
          onImage={(asset) => dispatch({ type: "patch", questionId: question.id, patch: { image: { ...image, asset } } })}
        />
        {image !== undefined && (
          <button type="button" className="designer-button designer-button--quiet" onClick={() => dispatch({ type: "patch", questionId: question.id, patch: {}, remove: ["image"] })}>
            {t("removeImage")}
          </button>
        )}
      </div>
      {image !== undefined && (
        <AltText
          t={t}
          state={state}
          id="inspector-question-image-alt"
          value={image.alt}
          onChange={(alt) => dispatch({ type: "patch", questionId: question.id, patch: { image: { ...image, alt } } })}
        />
      )}
    </div>
  );
}

/** An option's picture: upload or remove; the option's label is its alt text. */
function OptionPicture({ t, dispatch, question, index, n, asset }: Pick<RegionProps, "t" | "dispatch"> & { question: Question; index: number; n: number; asset: string | undefined }) {
  const { url } = useImages();

  // A 40 px slot before the option (W31): a dashed "+" to add a picture, or the picture with its own × to remove it.
  return asset === undefined ? (
    <span className="designer-items__picture">
      <ImageButton
        className="designer-items__add-picture"
        label="+"
        title={t("addPictureFor", { n })}
        fileLabel={t("pictureFor", { n })}
        onImage={(chosen) => dispatch({ type: "setOptionImage", questionId: question.id, index, asset: chosen })}
      />
    </span>
  ) : (
    <span className="designer-items__picture">
      <img src={url(asset, 160)} alt="" />
      <button type="button" className="designer-items__picture-remove" aria-label={t("removePicture", { n })} onClick={() => dispatch({ type: "setOptionImage", questionId: question.id, index, asset: null })}>
        ×
      </button>
    </span>
  );
}

/** The selected page (W17 A): its title as text (edited on the canvas), translations, questions, logic, move; Duplicate and Delete sit by its title (W23). */
function PageSettings({ t, state, dispatch, pageIndex }: Pick<InspectorProps, "t" | "state" | "dispatch"> & { pageIndex: number }) {
  const { definition, language } = state;
  const page = definition.pages[pageIndex]!;
  const count = page.questions.length;
  const title = pageName(t, page, pageIndex, language, definition);

  return (
    <aside className="designer-inspector" aria-label={t("pageSettings")}>
      <div className="designer-inspector__panel">
        <h2 className="designer-inspector__title">{t("pageOf", { number: pageIndex + 1, total: definition.pages.length })}</h2>
        <p className="designer-label">{t("pageTitleLabel")}</p>
        <p>{t("pageTitleHint", { title })}</p>
        <p className="designer-label">{t("pageTranslations")}</p>
        <p>{definition.languages.map((each) => `${each.toUpperCase()} ${page.title?.[each] !== undefined ? "✓" : "—"}`).join(" · ")}</p>
        <p className="designer-label">{t("pageQuestionsLabel")}</p>
        <p>{count === 0 ? t("pageQuestionsNone") : count === 1 ? t("pageQuestionsOne") : t("pageQuestions", { count })}</p>
        <PageLogic t={t} state={state} dispatch={dispatch} page={page} pageIndex={pageIndex} />
        <div className="designer-row">
          <button type="button" className="designer-button designer-button--quiet" disabled={pageIndex === 0} onClick={() => dispatch({ type: "movePage", pageId: page.id, toIndex: pageIndex - 1 })}>
            {t("pageMoveUp")}
          </button>
          <button
            type="button"
            className="designer-button designer-button--quiet"
            disabled={pageIndex === definition.pages.length - 1}
            onClick={() => dispatch({ type: "movePage", pageId: page.id, toIndex: pageIndex + 1 })}
          >
            {t("pageMoveDown")}
          </button>
        </div>
      </div>
    </aside>
  );
}
