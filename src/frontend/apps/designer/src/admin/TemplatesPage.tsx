// SPDX-License-Identifier: AGPL-3.0-only
import "./admin.css";
import type { SampleSurvey, SurveysClient, SurveyTemplate } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ConfirmDialog, DataGrid, dataGridTexts, type DataGridColumn } from "@askwerk/ui";
import { useCallback, useEffect, useMemo, useState } from "react";
import { adminMessages } from "./messages";

type Row = { id: string; name: string; from: "askwerk" | "organisation" | "sample"; questions: number; languages: string[] };

/**
 * Templates (AWK-38, W29): Askwerk's templates, the samples, and the organisation's own, each one click from a new
 * survey. The organisation's own can be deleted by those who edit surveys.
 */
export function TemplatesPage({ client, locale, canEdit, onOpen }: { client: Pick<SurveysClient, "listTemplates" | "listSamples" | "create" | "createSample" | "deleteTemplate">; locale: Locale; canEdit: boolean; onOpen: (surveyId: string) => void }) {
  const t = useMemo(() => createTranslator(adminMessages, locale), [locale]);
  const [rows, setRows] = useState<Row[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [removing, setRemoving] = useState<Row | null>(null);
  const text = (names: Record<string, string> | null | undefined, fallback: string) => names?.[locale] ?? names?.en ?? fallback;

  const load = useCallback(() => {
    Promise.all([client.listTemplates(), client.listSamples().catch(() => [] as SampleSurvey[])]).then(
      ([templates, samples]: [SurveyTemplate[], SampleSurvey[]]) =>
        setRows([
          ...templates.map((template) => ({
            id: template.id,
            name: template.name ?? text(template.names, template.id),
            from: template.scope,
            questions: template.questions,
            languages: template.languages,
          })),
          ...samples.map((sample) => ({ id: sample.id, name: text(sample.names, sample.id), from: "sample" as const, questions: sample.questions, languages: sample.languages })),
        ]),
      () => setFailed(true),
    );
    // text depends on locale only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, locale]);

  useEffect(load, [load]);

  const use = (row: Row) =>
    void (row.from === "sample" ? client.createSample(row.id, row.name) : client.create({ title: row.name, languages: [], defaultLanguage: "", templateId: row.id })).then(onOpen);

  const columns: DataGridColumn<Row>[] = [
    { key: "name", header: t("colTemplate"), phone: "title", cell: (row) => <b>{row.name}</b> },
    { key: "from", header: t("colFrom"), cell: (row) => t(row.from === "askwerk" ? "fromAskwerk" : row.from === "sample" ? "fromSample" : "fromOrganisation") },
    { key: "questions", header: t("colQuestions"), align: "end", cell: (row) => row.questions },
    { key: "languages", header: t("colLanguages"), cell: (row) => <span className="admin__mono">{row.languages.map((language) => language.toUpperCase()).join(" ")}</span> },
    {
      key: "actions",
      header: t("colActions"),
      align: "end",
      cell: (row) =>
        canEdit && (
          <span className="admin__row" style={{ justifyContent: "flex-end" }}>
            {row.from === "organisation" && (
              <button type="button" className="admin__linkbutton" onClick={() => setRemoving(row)}>
                {t("deleteTemplate")}
              </button>
            )}
            <button type="button" className="admin__button" aria-label={t("useTemplate", { name: row.name })} onClick={() => use(row)}>
              {t("use")}
            </button>
          </span>
        ),
    },
  ];

  return (
    <main className="admin admin--wide" lang={locale}>
      <h1>{t("templates")}</h1>
      <p className="admin__lead">{t("templatesLead")}</p>
      <div style={{ height: 18 }} />
      <DataGrid label={t("templates")} columns={columns} rows={rows} rowKey={(row) => `${row.from}:${row.id}`} empty={<p>{t("noTemplates")}</p>} error={failed} onRetry={load} texts={dataGridTexts(locale === "de" ? "de" : "en")} />
      {removing !== null && (
        <ConfirmDialog
          title={t("deleteTemplate")}
          text={t("deleteTemplateAsk", { name: removing.name })}
          confirm={t("deleteTemplate")}
          cancel={t("cancel")}
          onCancel={() => setRemoving(null)}
          onConfirm={() => void client.deleteTemplate(removing.id).then(() => (setRemoving(null), load()))}
        />
      )}
    </main>
  );
}
