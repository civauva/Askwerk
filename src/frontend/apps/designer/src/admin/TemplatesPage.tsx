// SPDX-License-Identifier: AGPL-3.0-only
import "./admin.css";
import type { SampleSurvey, SurveyFront, SurveysClient, SurveyTemplate } from "@askwerk/app-client";
import { createTranslator, type Locale, type ThemeTokens } from "@askwerk/schema";
import { ConfirmDialog, DataGrid, dataGridTexts, Dialog, type DataGridColumn } from "@askwerk/ui";
import { useCallback, useEffect, useId, useMemo, useState, type FormEvent } from "react";
import { browserStorage, type PreferenceStorage } from "../account/AccountApp";
import { ContentCard } from "../surveys/ContentCard";
import { viewKey, type ListView } from "../surveys/SurveyListPage";
import { adminMessages } from "./messages";
import { PageBar } from "../shell/PageBar";

type From = "askwerk" | "organisation" | "sample";
type Row = { id: string; name: string; from: From; questions: number; languages: string[]; front: SurveyFront | null };
type Filter = "all" | From;

type TemplatesClient = Pick<SurveysClient, "listTemplates" | "listSamples" | "create" | "createSample" | "deleteTemplate" | "editTemplate" | "copyTemplate" | "newTemplate">;

/**
 * Templates (AWK-38, W29, W32): Askwerk's templates, the samples, and the organisation's own, each one click from a new
 * survey, as a list or as cards drawn in their looks. The organisation's own open in the designer to edit; Askwerk's
 * stay as they are and copy to ours.
 */
export function TemplatesPage({
  client,
  locale,
  canEdit,
  onOpen,
  userId = "",
  organisation = "",
  storage = browserStorage,
}: {
  client: TemplatesClient;
  locale: Locale;
  canEdit: boolean;
  /** Opens a survey: a new one made from a template, or the one a template is edited in. */
  onOpen: (surveyId: string) => void;
  /** Whose List or Cards choice to remember. */
  userId?: string;
  organisation?: string;
  storage?: PreferenceStorage;
}) {
  const t = useMemo(() => createTranslator(adminMessages, locale), [locale]);
  const [rows, setRows] = useState<Row[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [removing, setRemoving] = useState<Row | null>(null);
  const [creating, setCreating] = useState(false);
  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");
  const [view, setView] = useState<ListView>(() => (storage.get(viewKey(userId, "templates")) === "cards" ? "cards" : "list"));
  const text = (names: Record<string, string> | null | undefined, fallback: string) => names?.[locale] ?? names?.en ?? fallback;

  const load = useCallback(() => {
    Promise.all([client.listTemplates(), client.listSamples<ThemeTokens>().catch(() => [] as SampleSurvey<ThemeTokens>[])]).then(
      ([templates, samples]: [SurveyTemplate[], SampleSurvey<ThemeTokens>[]]) =>
        setRows([
          ...templates.map((template) => ({
            id: template.id,
            name: template.name ?? text(template.names, template.id),
            from: template.scope,
            questions: template.questions,
            languages: template.languages,
            front: template.front ?? null,
          })),
          ...samples.map((sample) => ({
            id: sample.id,
            name: text(sample.names, sample.id),
            from: "sample" as const,
            questions: sample.questions,
            languages: sample.languages,
            // Like a template's card (W32): its welcome page and what it asks.
            front: sample.front ?? { title: sample.names, text: sample.kind, defaultLanguage: "en", look: sample.tokens, lookPreset: sample.look },
          })),
        ]),
      () => setFailed(true),
    );
    // text depends on locale only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, locale]);

  useEffect(load, [load]);

  const use = (row: Row) =>
    void (row.from === "sample" ? client.createSample(row.id, row.name) : client.create({ title: row.name, languages: [], defaultLanguage: "", templateId: row.id })).then(onOpen);
  const edit = (row: Row) => void (row.from === "askwerk" ? client.copyTemplate(row.id, locale) : client.editTemplate(row.id)).then((opened) => onOpen(opened.surveyId));
  const changeView = (next: ListView) => {
    setView(next);
    storage.set(viewKey(userId, "templates"), next);
  };
  const fromText = (from: From) => t(from === "askwerk" ? "fromAskwerk" : from === "sample" ? "fromSample" : "fromOrganisation");
  const shown = (rows ?? []).filter((row) => (filter === "all" || row.from === filter) && row.name.toLowerCase().includes(search.trim().toLowerCase()));
  const counts = (key: Filter) => (rows ?? []).filter((row) => key === "all" || row.from === key).length;
  // A card's one action (W32): ours are edited, Askwerk's and the samples are used.
  const main = (row: Row) => (row.from === "organisation" ? () => edit(row) : () => use(row));
  const mainLabel = (row: Row) => (row.from === "organisation" ? t("editTemplateNamed", { name: row.name }) : t("useTemplate", { name: row.name }));

  const actions = (row: Row) =>
    canEdit && (
      <span className="admin__row" style={{ justifyContent: "flex-end" }}>
        {row.from === "organisation" && (
          <button type="button" className="admin__linkbutton" onClick={() => setRemoving(row)}>
            {t("deleteTemplate")}
          </button>
        )}
        {row.from !== "sample" && (
          <button type="button" className="admin__button" aria-label={t(row.from === "askwerk" ? "copyTemplateNamed" : "editTemplateNamed", { name: row.name })} onClick={() => edit(row)}>
            {t(row.from === "askwerk" ? "copyTemplate" : "editTemplate")}
          </button>
        )}
        <button type="button" className="admin__button" aria-label={t("useTemplate", { name: row.name })} onClick={() => use(row)}>
          {t("use")}
        </button>
      </span>
    );

  const columns: DataGridColumn<Row>[] = [
    { key: "name", header: t("colTemplate"), phone: "title", cell: (row) => <b>{row.name}</b> },
    { key: "from", header: t("colFrom"), cell: (row) => fromText(row.from) },
    { key: "questions", header: t("colQuestions"), align: "end", cell: (row) => row.questions },
    { key: "languages", header: t("colLanguages"), cell: (row) => <span className="admin__mono">{row.languages.map((language) => language.toUpperCase()).join(" ")}</span> },
    { key: "actions", header: t("colActions"), align: "end", cell: actions },
  ];
  const filters: [Filter, Parameters<typeof t>[0]][] = [
    ["all", "templatesAll"],
    ["askwerk", "templatesAskwerk"],
    ["organisation", "templatesOurs"],
    ["sample", "templatesSamples"],
  ];

  return (
    <main className="admin admin--wide" lang={locale}>
      <PageBar
        title={t("templates")}
        actions={
          canEdit ? (
            <button type="button" className="admin__button" onClick={() => setCreating(true)}>
              {t("newTemplate")}
            </button>
          ) : undefined
        }
      />
      <div className="admin__head">
        <h1>{t("templates")}</h1>
      </div>
      <div className="admin__tools">
        <input type="search" className="admin__search" aria-label={t("searchTemplates")} placeholder={t("searchTemplates")} value={search} onChange={(event) => setSearch(event.target.value)} />
        <span className="admin__chips" role="group" aria-label={t("colFrom")}>
          {filters.map(([key, label]) => (
            <button key={key} type="button" aria-pressed={filter === key} onClick={() => setFilter(key)}>
              {t(label, { count: counts(key) })}
            </button>
          ))}
        </span>
        <span className="view-switch" role="group" aria-label={t("view")}>
          <button type="button" aria-pressed={view === "list"} onClick={() => changeView("list")}>
            {t("viewList")}
          </button>
          <button type="button" aria-pressed={view === "cards"} onClick={() => changeView("cards")}>
            {t("viewCards")}
          </button>
        </span>
      </div>
      {view === "list" || rows === null ? (
        <DataGrid label={t("templates")} columns={columns} rows={rows === null ? null : shown} rowKey={(row) => `${row.from}:${row.id}`} empty={<p>{t("noTemplates")}</p>} error={failed} onRetry={load} texts={dataGridTexts(locale === "de" ? "de" : "en")} />
      ) : shown.length === 0 ? (
        <p>{t("noTemplates")}</p>
      ) : (
        <ul className="front-cards" aria-label={t("templates")}>
          {shown.map((row) => (
            <ContentCard
              key={`${row.from}:${row.id}`}
              front={row.front ?? { title: null, text: null, defaultLanguage: "en", look: {} }}
              name={row.name}
              locale={locale}
              organisation={organisation}
              {...(canEdit ? { onOpen: main(row), openLabel: mainLabel(row) } : {})}
              footer={
                <>
                  <span className="front-card__chip">{fromText(row.from)}</span>
                  <span>{row.questions === 1 ? t("questionOne") : t("questionsCount", { count: row.questions })}</span>
                  <span className="grow" />
                  {canEdit && (
                    <button type="button" className="admin__button admin__button--small" aria-label={mainLabel(row)} onClick={main(row)}>
                      {row.from === "organisation" ? t("editTemplate") : t("use")}
                    </button>
                  )}
                </>
              }
            />
          ))}
        </ul>
      )}
      {creating && <NewTemplateDialog client={client} locale={locale} onCancel={() => setCreating(false)} onCreated={onOpen} />}
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

/** New template (W32): a name, then the designer opens on the empty template. */
function NewTemplateDialog({ client, locale, onCancel, onCreated }: { client: TemplatesClient; locale: Locale; onCancel: () => void; onCreated: (surveyId: string) => void }) {
  const t = createTranslator(adminMessages, locale);
  const id = useId();
  const [name, setName] = useState("");
  const [error, setError] = useState<"nameRequired" | "somethingWrong" | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = (event: FormEvent) => {
    event.preventDefault();

    if (name.trim() === "") {
      setError("nameRequired");
      return;
    }

    setBusy(true);
    client.newTemplate(name.trim(), locale).then(
      (made) => onCreated(made.surveyId),
      () => {
        setError("somethingWrong");
        setBusy(false);
      },
    );
  };

  return (
    <Dialog title={t("newTemplate")} onClose={onCancel}>
      <form className="admin__form" onSubmit={submit} noValidate>
        <label className="admin__label" htmlFor={`${id}-name`}>
          {t("name")}
        </label>
        <input
          id={`${id}-name`}
          className="admin__input"
          autoFocus
          maxLength={200}
          value={name}
          aria-invalid={error === "nameRequired"}
          aria-describedby={error !== null ? `${id}-error` : undefined}
          onChange={(event) => setName(event.target.value)}
        />
        {error !== null && (
          <span id={`${id}-error`} className="admin__error" role="alert">
            {t(error)}
          </span>
        )}
        <div className="admin__row" style={{ justifyContent: "flex-end" }}>
          <button type="button" className="admin__button" onClick={onCancel}>
            {t("cancel")}
          </button>
          <button type="submit" className="admin__button admin__button--primary" disabled={busy}>
            {t("createTemplate")}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
