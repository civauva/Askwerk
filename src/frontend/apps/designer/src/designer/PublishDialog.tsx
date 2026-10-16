// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError } from "@askwerk/app-client";
import { checkDefinition, variableRenames, type DefinitionIssue, type LanguageCode, type SurveyDefinition, type Translate } from "@askwerk/schema";
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode, type Ref } from "react";
import type { DesignerApi, PublishedSurvey, SurveyDraft } from "./api";
import type { DesignerMessageKey } from "./messages";
import { allQuestions } from "./state";
import { languageName } from "./text";
import { Published } from "./Published";
import { missingTexts } from "./translations";

type PublishDialogProps = {
  t: Translate<DesignerMessageKey>;
  api: DesignerApi;
  draft: SurveyDraft;
  definition: SurveyDefinition;
  writeClipboard: (text: string) => Promise<void>;
  saveFile: (name: string, file: Blob) => void;
  /** The language being edited: the PDF and the embed code use it. */
  language: string;
  participants?: import("@askwerk/app-client").ParticipantsClient | undefined;
  previewInvitation?: ((language: string) => Promise<{ subject: string; text: string }>) | undefined;
  locale?: import("@askwerk/schema").Locale;
  /** Whether the draft is approved (AWK-28, W16 D); publishing never waits for it. */
  approval?: string | undefined;
  onFixTranslations: (language: LanguageCode) => void;
  onClose: () => void;
};

type Step =
  | { kind: "check" }
  | { kind: "publishing" }
  | { kind: "published"; result: PublishedSurvey }
  | { kind: "failed"; traceId: string | undefined };

/** Publish check with the large missing-translation warning, the published links, and failure (M3 in AWK-5). */
export function PublishDialog({ t, api, draft, definition, writeClipboard, saveFile, language, participants, previewInvitation, locale, approval, onFixTranslations, onClose }: PublishDialogProps) {
  const [step, setStep] = useState<Step>({ kind: "check" });
  const [copied, setCopied] = useState<string | null>(null);
  const dialog = useRef<HTMLDivElement>(null);
  const previous = draft.published ?? undefined;
  const issues = checkDefinition(definition, previous);
  const errors = issues.filter((issue) => issue.severity === "error");
  const missing = missingTexts(definition);
  const renames = previous === undefined ? [] : variableRenames(previous, definition);
  const questions = allQuestions(definition);
  const previousVersion = draft.published === null ? undefined : draft.versionNumber - 1;

  useEffect(() => {
    dialog.current?.focus();
  }, [step.kind]);

  function publish() {
    setStep({ kind: "publishing" });
    api.publish(draft.surveyId).then(
      (result) => setStep({ kind: "published", result }),
      (error: unknown) => setStep({ kind: "failed", traceId: error instanceof ApiError ? error.traceId : undefined }),
    );
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === "Escape" && step.kind !== "publishing") {
      onClose();
    }
  }

  if (step.kind === "failed") {
    return (
      <Modal ref={dialog} role="alertdialog" titleId="publish-failed" onKeyDown={onKeyDown} className="designer-dialog designer-dialog--failed">
        <div className="designer-dialog__head">
          <span className="designer-meta designer-meta--bad">{t("notPublished")}</span>
          <h2 id="publish-failed">{t("publishFailedTitle")}</h2>
        </div>
        <div className="designer-dialog__body">
          <p>{previousVersion === undefined ? t("publishFailedFirst") : t("publishFailedText", { version: previousVersion })}</p>
          {step.traceId !== undefined && <code>{t("saveReference", { traceId: step.traceId })}</code>}
        </div>
        <div className="designer-dialog__foot">
          {step.traceId !== undefined && (
            <button type="button" className="designer-button" onClick={() => writeClipboard(step.traceId ?? "").catch(() => undefined)}>
              {t("copyReference")}
            </button>
          )}
          <button type="button" className="designer-button designer-button--primary" onClick={publish}>
            {t("tryAgain")}
          </button>
        </div>
      </Modal>
    );
  }

  if (step.kind === "published") {
    return (
      <Modal ref={dialog} role="dialog" titleId="publish-done" onKeyDown={onKeyDown} className="designer-dialog designer-dialog--wide">
        <div className="designer-dialog__head">
          <span className="designer-meta">{t("liveVersion", { version: step.result.versionNumber })}</span>
          <h2 id="publish-done">{t("publishedTitle")}</h2>
        </div>
        <div className="designer-dialog__body">
          <Published
            t={t}
            surveyId={draft.surveyId}
            title={draft.title}
            language={language}
            languages={definition.languages}
            links={step.result.links}
            writeClipboard={writeClipboard}
            saveFile={saveFile}
            participants={participants}
            preview={previewInvitation}
            {...(locale === undefined ? {} : { locale })}
          />
        </div>
        <div className="designer-dialog__foot">
          <button type="button" className="designer-button designer-button--primary" onClick={onClose}>
            {t("done")}
          </button>
        </div>
      </Modal>
    );
  }

  const missingByLanguage = definition.languages
    .map((language) => ({ language, texts: missing.filter((text) => text.language === language) }))
    .filter((group) => group.texts.length > 0);

  return (
    <Modal ref={dialog} role="dialog" titleId="publish-check" onKeyDown={onKeyDown} className="designer-dialog">
      <div className="designer-dialog__head">
        <span className="designer-meta">{t("publishVersion", { version: draft.versionNumber })}</span>
        <h2 id="publish-check">{t("publishTitle", { title: draft.title })}</h2>
      </div>
      <div className="designer-dialog__body">
        {approval !== undefined && <p className="designer-note">{approval}</p>}
        {errors.length === 0 ? (
          <ul className="designer-checks">
            <li>{t("checkComplete", { questions: questions.length, pages: definition.pages.length })}</li>
            <li>
              {renames.length === 0
                ? t("checkVariables")
                : t(renames.length === 1 ? "checkVariablesRenamedOne" : "checkVariablesRenamed", {
                    count: renames.length,
                    renames: renames.map((rename) => `${rename.from} → ${rename.to}`).join(", "),
                  })}
            </li>
          </ul>
        ) : (
          <div className="designer-errors" role="group" aria-labelledby="publish-errors">
            <h3 id="publish-errors">{t("fixBeforePublishing")}</h3>
            <ul>
              {errors.map((issue) => (
                <li key={`${issue.code}-${issue.path}`}>{describeError(t, issue, definition)}</li>
              ))}
            </ul>
          </div>
        )}
        {missingByLanguage.map((group) => (
          <section key={group.language} className="designer-bigwarn" aria-labelledby={`missing-warning-${group.language}`}>
            <span className="designer-bigwarn__icon" aria-hidden="true">
              !
            </span>
            <h3 id={`missing-warning-${group.language}`}>
              {t(group.texts.length === 1 ? "missingWarningTitleOne" : "missingWarningTitle", {
                count: group.texts.length,
                language: languageName(t, group.language),
              })}
            </h3>
            <p>
              {t("missingWarningText", {
                language: languageName(t, group.language),
                fallback: languageName(t, definition.defaultLanguage),
              })}
            </p>
            <ul>
              {group.texts.map((text) => (
                <li key={text.path}>
                  {text.source} ({text.reference})
                </li>
              ))}
            </ul>
            <div>
              <button type="button" className="designer-button" onClick={() => onFixTranslations(group.language)}>
                {t("fixTranslations")}
              </button>
            </div>
          </section>
        ))}
        <p className="designer-note">
          {previousVersion === undefined
            ? t("frozenFirst", { version: draft.versionNumber })
            : t("frozen", { version: draft.versionNumber, previous: previousVersion })}
        </p>
      </div>
      <div className="designer-dialog__foot">
        <button type="button" className="designer-button" onClick={onClose}>
          {t("cancel")}
        </button>
        <button
          type="button"
          className="designer-button designer-button--primary"
          disabled={errors.length > 0 || step.kind === "publishing"}
          onClick={publish}
        >
          {missing.length > 0 ? t("publishWithMissing") : t("publish")}
        </button>
      </div>
    </Modal>
  );
}

type ModalProps = {
  ref: Ref<HTMLDivElement>;
  role: "dialog" | "alertdialog";
  titleId: string;
  className: string;
  onKeyDown: (event: KeyboardEvent) => void;
  children: ReactNode;
};

function Modal({ ref, role, titleId, className, onKeyDown, children }: ModalProps) {
  return (
    <div className="aw-backdrop">
      <div ref={ref} role={role} aria-modal="true" aria-labelledby={titleId} tabIndex={-1} className={className} onKeyDown={onKeyDown}>
        {children}
      </div>
    </div>
  );
}

function describeError(t: Translate<DesignerMessageKey>, issue: DefinitionIssue, definition: SurveyDefinition): string {
  const [first, pageIndex, second, questionIndex] = issue.path.split("/");
  const page = first === "pages" ? definition.pages[Number(pageIndex)] : undefined;
  const question = second === "questions" ? page?.questions[Number(questionIndex)] : undefined;
  const reference = question !== undefined ? `Q${allQuestions(definition).indexOf(question) + 1}` : page !== undefined ? `P${Number(pageIndex) + 1}` : "";
  const variable = question?.variable ?? "";

  switch (issue.code) {
    case "survey-empty":
      return t("errorSurveyEmpty");
    case "default-language-missing":
      return t("errorDefaultLanguage");
    case "variable-invalid":
      return t("errorVariableInvalid", { variable, reference });
    case "variable-duplicate":
      return t("errorVariableDuplicate", { variable, reference });
    case "variable-reused":
      return t("errorVariableReused", { variable, reference });
    case "text-missing":
      return t("errorTextMissing", { language: languageName(t, issue.language ?? definition.defaultLanguage), reference: reference || "END" });
    case "options-missing":
      return t("errorOptionsMissing", { reference });
    case "rows-missing":
      return t("errorRowsMissing", { reference });
    case "scale-invalid":
      return t("errorScaleInvalid", { reference });
    case "rating-steps-too-many":
      return t("errorRatingSteps", { reference });
    case "faces-need-three-or-five-steps":
      return t("errorFacesSteps", { reference });
    case "slider-range-invalid":
      return t("errorSliderRange", { reference });
    case "other-code-taken":
      return t("errorOtherCode", { reference });
    case "other-variable-taken":
      return t("errorOtherVariable", { reference });
    case "other-not-allowed":
      return t("errorOtherNotAllowed", { reference });
    case "yes-no-codes-equal":
      return t("errorYesNoCodes", { reference });
    case "date-value-invalid":
      return t("errorDateValue", { reference });
    case "date-range-invalid":
      return t("errorDateRange", { reference });
    case "minute-step-invalid":
      return t("errorMinuteStep", { reference });
    default:
      return t("errorCodeDuplicate", { reference });
  }
}
