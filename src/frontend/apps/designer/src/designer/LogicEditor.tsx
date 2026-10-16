// SPDX-License-Identifier: AGPL-3.0-only
// The logic editor (AWK-31, W19): conditions built from earlier questions and read back as sentences.
import type { LogicCondition, LogicGroup, LogicOperator, Page, PageAfter, Question, SurveyDefinition } from "@askwerk/schema";
import { ComboBox } from "@askwerk/ui";
import { useId } from "react";
import { allQuestionsOf, defaultCondition, pageCandidates, questionCandidates } from "./logicModel";
import { afterText, choicesOf, conditionsText, isNumeric, operatorKey, operatorsFor } from "./logicText";
import { pageName } from "./Outline";
import type { RegionProps } from "./text";

type T = RegionProps["t"];

const comboTexts = (t: T, choose: string) => ({ choose, search: t("comboSearch"), results: (shown: number, total: number) => t("comboResults", { shown, total }), noResults: t("comboNoResults") });

/** Conditions joined by all or any, each built from a question, a condition and its answer (W19 A). */
function LogicBuilder({ t, state, group, candidates, onChange }: Pick<RegionProps, "t" | "state"> & { group: LogicGroup; candidates: readonly Question[]; onChange: (group: LogicGroup | null) => void }) {
  const id = useId();
  const { definition, language } = state;
  const update = (index: number, condition: LogicCondition) => onChange({ ...group, conditions: group.conditions.map((each, at) => (at === index ? condition : each)) });

  return (
    <div className="designer-logic">
      <span id={`${id}-match`} className="designer-visually-hidden">
        {t("logicMatch")}
      </span>
      <ComboBox
        labelledBy={`${id}-match`}
        options={[
          { value: "all", label: t("logicAll") },
          { value: "any", label: t("logicAny") },
        ]}
        value={group.match}
        onChange={(match) => onChange({ ...group, match: match as LogicGroup["match"] })}
        texts={comboTexts(t, t("logicMatch"))}
      />
      {group.conditions.map((condition, index) => (
        <ConditionRow
          key={index}
          t={t}
          definition={definition}
          language={language}
          number={index + 1}
          condition={condition}
          candidates={candidates}
          onChange={(next) => update(index, next)}
          onRemove={() => onChange(group.conditions.length === 1 ? null : { ...group, conditions: group.conditions.filter((_, at) => at !== index) })}
        />
      ))}
      <button type="button" className="designer-link-button" onClick={() => onChange({ ...group, conditions: [...group.conditions, defaultCondition(candidates[candidates.length - 1]!)] })}>
        {t("logicAdd")}
      </button>
    </div>
  );
}

function ConditionRow({
  t,
  definition,
  language,
  number,
  condition,
  candidates,
  onChange,
  onRemove,
}: {
  t: T;
  definition: SurveyDefinition;
  language: string;
  number: number;
  condition: LogicCondition;
  candidates: readonly Question[];
  onChange: (condition: LogicCondition) => void;
  onRemove: () => void;
}) {
  const id = useId();
  const question = allQuestionsOf(definition).find((each) => each.id === condition.question) ?? candidates[0]!;
  const choices = choicesOf(t, question, language, definition);
  const multi = condition.op === "is-one-of" || condition.op === "includes-any" || condition.op === "includes-none";
  const single = !isNumeric(question) && (condition.op === "is" || condition.op === "is-not" || condition.op === "includes");
  const numbered = isNumeric(question) && condition.op !== "answered" && condition.op !== "not-answered";

  const setOp = (op: LogicOperator) => {
    const base = { question: question.id, op };

    if (op === "answered" || op === "not-answered") {
      onChange(base);
    } else if (isNumeric(question)) {
      const number = condition.number ?? defaultCondition(question).number ?? 0;
      onChange(op === "between" ? { ...base, number, to: condition.to ?? number } : { ...base, number });
    } else if (op === "is-one-of" || op === "includes-any" || op === "includes-none") {
      onChange({ ...base, values: condition.values ?? (condition.value !== undefined ? [condition.value] : []) });
    } else {
      onChange({ ...base, value: condition.value ?? choices[0]?.code ?? "" });
    }
  };

  return (
    <div className="designer-logic__row">
      <span id={`${id}-question`} className="designer-visually-hidden">
        {t("logicConditionQuestion", { number })}
      </span>
      <ComboBox
        labelledBy={`${id}-question`}
        options={candidates.map((each) => ({ value: each.id, label: each.variable }))}
        value={question.id}
        onChange={(questionId) => onChange(defaultCondition(candidates.find((each) => each.id === questionId)!))}
        texts={comboTexts(t, t("logicConditionQuestion", { number }))}
      />
      <span id={`${id}-op`} className="designer-visually-hidden">
        {t("logicConditionOp", { number })}
      </span>
      <ComboBox
        labelledBy={`${id}-op`}
        options={operatorsFor(question.type).map((op) => ({ value: op, label: t(operatorKey[op]) }))}
        value={condition.op}
        onChange={(op) => setOp(op as LogicOperator)}
        texts={comboTexts(t, t("logicConditionOp", { number }))}
      />
      {single && (
        <>
          <span id={`${id}-value`} className="designer-visually-hidden">
            {t("logicConditionValue", { number })}
          </span>
          <ComboBox
            labelledBy={`${id}-value`}
            options={choices.map((choice) => ({ value: choice.code, label: choice.label }))}
            value={condition.value ?? ""}
            onChange={(value) => onChange({ ...condition, value })}
            texts={comboTexts(t, t("logicConditionValue", { number }))}
          />
        </>
      )}
      {multi && (
        <fieldset className="designer-logic__values">
          <legend className="designer-visually-hidden">{t("logicConditionValue", { number })}</legend>
          {choices.map((choice) => (
            <label key={choice.code} className="designer-check">
              <input
                type="checkbox"
                checked={(condition.values ?? []).includes(choice.code)}
                onChange={(event) =>
                  onChange({ ...condition, values: event.target.checked ? [...(condition.values ?? []), choice.code] : (condition.values ?? []).filter((code) => code !== choice.code) })
                }
              />
              {choice.label}
            </label>
          ))}
        </fieldset>
      )}
      {numbered && (
        <input
          type="number"
          className="designer-input designer-logic__number"
          aria-label={t("logicConditionNumber", { number })}
          value={condition.number ?? ""}
          onChange={(event) => event.target.value !== "" && onChange({ ...condition, number: Number(event.target.value) })}
        />
      )}
      {numbered && condition.op === "between" && (
        <input
          type="number"
          className="designer-input designer-logic__number"
          aria-label={t("logicConditionTo", { number })}
          value={condition.to ?? ""}
          onChange={(event) => event.target.value !== "" && onChange({ ...condition, to: Number(event.target.value) })}
        />
      )}
      <button type="button" className="designer-icon-button" aria-label={t("logicRemove", { number })} onClick={onRemove}>
        ✕
      </button>
    </div>
  );
}

/** "Show this question: Always / Only when …" in the question's settings (W19 A). */
export function QuestionLogic({ t, state, dispatch, question }: RegionProps & { question: Question }) {
  const id = useId();
  const candidates = questionCandidates(state.definition, question.id);
  const group = question.showWhen;
  const set = (next: LogicGroup | null) =>
    dispatch(next === null ? { type: "patch", questionId: question.id, patch: {}, remove: ["showWhen"] } : { type: "patch", questionId: question.id, patch: { showWhen: next } });

  return (
    <fieldset className="designer-radios designer-logic__section">
      <legend className="designer-label">{t("logicShowQuestion")}</legend>
      <label className="designer-radio">
        <input type="radio" name={`${id}-show`} checked={group === undefined} onChange={() => set(null)} />
        {t("logicAlways")}
      </label>
      <label className="designer-radio">
        <input type="radio" name={`${id}-show`} checked={group !== undefined} disabled={candidates.length === 0} onChange={() => set({ match: "all", conditions: [defaultCondition(candidates[candidates.length - 1]!)] })} />
        {t("logicOnlyWhen")}
      </label>
      {candidates.length === 0 && <p className="designer-note">{t("logicNone")}</p>}
      {group !== undefined && candidates.length > 0 && (
        <>
          <LogicBuilder t={t} state={state} group={group} candidates={candidates} onChange={set} />
          <p className="designer-logic__reads">{t("logicReads", { sentence: t("logicShownWhen", { conditions: conditionsText(t, group, state.definition, state.language) }) })}</p>
        </>
      )}
    </fieldset>
  );
}

/** A page's logic in its settings (W19 B): shown under conditions, and after it go on, jump forward or end. */
export function PageLogic({ t, state, dispatch, page, pageIndex }: RegionProps & { page: Page; pageIndex: number }) {
  const id = useId();
  const { definition, language } = state;
  const earlier = pageCandidates(definition, pageIndex, false);
  const ownAndEarlier = pageCandidates(definition, pageIndex, true);
  const later = definition.pages.slice(pageIndex + 1);
  const after = page.after;
  const mode = after === undefined ? "next" : after.when === undefined && after.end === true ? "end" : "jump";
  const setShow = (showWhen: LogicGroup | null) => dispatch({ type: "setPageLogic", pageId: page.id, showWhen });
  const setAfter = (next: PageAfter | null) => dispatch({ type: "setPageLogic", pageId: page.id, after: next });
  const nameOf = (pageId: string) => {
    const index = definition.pages.findIndex((each) => each.id === pageId);
    return index < 0 ? pageId : `${String(index + 1).padStart(2, "0")} · ${pageName(t, definition.pages[index]!, index, language, definition)}`;
  };
  const sentence = after === undefined ? null : afterText(t, after, definition, language, nameOf);

  return (
    <>
      <fieldset className="designer-radios designer-logic__section">
        <legend className="designer-label">{t("logicShowPage")}</legend>
        <label className="designer-radio">
          <input type="radio" name={`${id}-show`} checked={page.showWhen === undefined} onChange={() => setShow(null)} />
          {t("logicAlways")}
        </label>
        <label className="designer-radio">
          <input type="radio" name={`${id}-show`} checked={page.showWhen !== undefined} disabled={earlier.length === 0} onChange={() => setShow({ match: "all", conditions: [defaultCondition(earlier[earlier.length - 1]!)] })} />
          {t("logicOnlyWhen")}
        </label>
        {earlier.length === 0 && <p className="designer-note">{t("logicNonePage")}</p>}
        {page.showWhen !== undefined && earlier.length > 0 && (
          <>
            <LogicBuilder t={t} state={state} group={page.showWhen} candidates={earlier} onChange={setShow} />
            <p className="designer-logic__reads">{t("logicReads", { sentence: t("logicShownWhen", { conditions: conditionsText(t, page.showWhen, definition, language) }) })}</p>
          </>
        )}
      </fieldset>
      <fieldset className="designer-radios designer-logic__section">
        <legend className="designer-label">{t("afterPage")}</legend>
        <label className="designer-radio">
          <input type="radio" name={`${id}-after`} checked={mode === "next"} onChange={() => setAfter(null)} />
          {t("afterNext")}
        </label>
        <label className="designer-radio">
          <input
            type="radio"
            name={`${id}-after`}
            checked={mode === "jump"}
            disabled={ownAndEarlier.length === 0}
            onChange={() => setAfter({ when: { match: "all", conditions: [defaultCondition(ownAndEarlier[0]!)] }, ...(later[0] === undefined ? { end: true } : { goTo: later[later.length - 1]!.id }) })}
          />
          {t("afterJump")}
        </label>
        {mode === "jump" && after !== undefined && (
          <>
            <LogicBuilder
              t={t}
              state={state}
              group={after.when ?? { match: "all", conditions: [defaultCondition(ownAndEarlier[0]!)] }}
              candidates={ownAndEarlier}
              onChange={(when) => (when === null ? setAfter(null) : setAfter({ ...after, when }))}
            />
            <JumpTarget t={t} after={after} later={later} nameOf={nameOf} onChange={setAfter} />
          </>
        )}
        <label className="designer-radio">
          <input type="radio" name={`${id}-after`} checked={mode === "end"} onChange={() => setAfter({ end: true })} />
          {t("afterEnd")}
        </label>
        {sentence !== null && <p className="designer-logic__reads">{t("logicReads", { sentence })}</p>}
      </fieldset>
    </>
  );
}

function JumpTarget({ t, after, later, nameOf, onChange }: { t: T; after: PageAfter; later: readonly Page[]; nameOf: (pageId: string) => string; onChange: (after: PageAfter) => void }) {
  const id = useId();
  const { goTo: _goTo, end: _end, ...rest } = after;

  return (
    <div className="designer-logic__row">
      <span id={`${id}-then`} className="designer-label">
        {t("afterThen")}
      </span>
      <ComboBox
        labelledBy={`${id}-then`}
        options={[{ value: "", label: t("afterEnd") }, ...later.map((page) => ({ value: page.id, label: nameOf(page.id) }))]}
        value={after.end === true ? "" : (after.goTo ?? "")}
        onChange={(value) => onChange(value === "" ? { ...rest, end: true } : { ...rest, goTo: value })}
        texts={comboTexts(t, t("afterThen"))}
      />
    </div>
  );
}

/** Settings → Logic (W19 C): every rule as a sentence; Edit opens it where it lives. */
export function LogicOverview({ t, state, dispatch }: RegionProps) {
  const id = useId();
  const { definition, language } = state;
  const rules = definition.pages.flatMap((page, pageIndex) => {
    const pageLabel = `${String(pageIndex + 1).padStart(2, "0")} · ${pageName(t, page, pageIndex, language, definition)}`;
    const nameOf = (pageId: string) => {
      const index = definition.pages.findIndex((each) => each.id === pageId);
      return index < 0 ? pageId : `${String(index + 1).padStart(2, "0")} · ${pageName(t, definition.pages[index]!, index, language, definition)}`;
    };

    return [
      ...(page.showWhen === undefined ? [] : [{ key: `${page.id}-show`, target: pageLabel, text: t("logicShownWhen", { conditions: conditionsText(t, page.showWhen, definition, language) }), open: () => dispatch({ type: "selectPage", pageId: page.id }) }]),
      ...page.questions.flatMap((question) =>
        question.showWhen === undefined
          ? []
          : [{ key: question.id, target: question.variable, text: t("logicShownWhen", { conditions: conditionsText(t, question.showWhen, definition, language) }), open: () => dispatch({ type: "select", questionId: question.id }) }],
      ),
      ...(page.after === undefined
        ? []
        : [{ key: `${page.id}-after`, target: t("logicAfterTarget", { page: pageLabel }), text: afterText(t, page.after, definition, language, nameOf) ?? "", open: () => dispatch({ type: "selectPage", pageId: page.id }) }]),
    ];
  });

  return (
    <section className="designer-logic__overview" aria-labelledby={`${id}-title`}>
      <h3 id={`${id}-title`} className="designer-label">
        {rules.length === 1 ? t("logicOverviewOne") : t("logicOverview", { count: rules.length })}
      </h3>
      {rules.length === 0 ? (
        <p className="designer-note">{t("logicOverviewNone")}</p>
      ) : (
        <ul>
          {rules.map((rule) => (
            <li key={rule.key}>
              <span className="designer-mono">{rule.target}</span>
              <span>{rule.text}</span>
              <button type="button" className="designer-link-button" aria-label={t("logicEdit", { target: rule.target })} onClick={rule.open}>
                {t("logicEditShort")}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
