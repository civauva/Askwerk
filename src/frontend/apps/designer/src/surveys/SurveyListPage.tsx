// SPDX-License-Identifier: AGPL-3.0-only
import "./surveys.css";
import { ApiError, type Me, type SampleSurvey, type SurveyList, type SurveyListItem, type SurveysClient, type SurveyStatus, type SurveyTemplate } from "@askwerk/app-client";
import { createTranslator, themePresets, type Locale, type ThemeTokens, type Translate } from "@askwerk/schema";
import { LookThumb } from "../designer/look/LookThumb";
import { ComboBox, DataGrid, dataGridTexts, pageSizes, sortFromParam, sortToParam, type DataGridColumn, type DataGridSort, Dialog } from "@askwerk/ui";
import { useCallback, useEffect, useId, useRef, useState, type FormEvent } from "react";
import { browserStorage, type PreferenceStorage } from "../account/AccountApp";
import { surveysMessages, type SurveysMessageKey } from "./messages";

type SurveyListPageProps = {
  client: SurveysClient;
  me: Me;
  locale: Locale;
  onOpen: (surveyId: string) => void;
  /** Opens a survey's results (AWK-24); the response count becomes a button when given. */
  onOpenResults?: (surveyId: string) => void;
  /** The address's query (?page=2&sort=title&size=25), where the list keeps its place (W13 A). */
  address?: string;
  onAddress?: (query: string) => void;
  /** Remembers the page size on this device. */
  storage?: PreferenceStorage;
  now?: () => Date;
  timeZone?: string;
  searchDelayMs?: number;
};

const defaultSort = "-changed";
const defaultSize = 50;
const sizeKey = "askwerk.grid.surveys.size";

/** Page, sort and size from the address; the size falls back to the one remembered on this device. */
function readPlace(address: string, remembered: string | null) {
  const query = new URLSearchParams(address);
  const size = [query.get("size"), remembered].map(Number).find((value) => (pageSizes as readonly number[]).includes(value)) ?? defaultSize;
  const page = Math.max(1, Math.floor(Number(query.get("page")) || 1));

  return { page, sort: sortFromParam(query.get("sort") || defaultSort), size };
}

/** Only what differs from the defaults goes into the address. */
function writePlace(page: number, sort: DataGridSort, size: number): string {
  const query = new URLSearchParams();
  page > 1 && query.set("page", String(page));
  sortToParam(sort) !== defaultSort && query.set("sort", sortToParam(sort));
  size !== defaultSize && query.set("size", String(size));
  const text = query.toString();

  return text ? `?${text}` : "";
}

type Filter = "all" | Exclude<SurveyStatus, "archived">;

const filters: [Filter, SurveysMessageKey][] = [
  ["all", "filterAll"],
  ["draft", "filterDraft"],
  ["published", "filterPublished"],
  ["closed", "filterClosed"],
];

const editorRoles = ["owner", "admin", "editor"];

/** The organisation's surveys (AWK-15 step 3, W6): filters, search, rows that open the designer, and New survey. */
export function SurveyListPage({
  client,
  me,
  locale,
  onOpen,
  onOpenResults,
  address = "",
  onAddress,
  storage = browserStorage,
  now = () => new Date(),
  timeZone,
  searchDelayMs = 250,
}: SurveyListPageProps) {
  const t = createTranslator(surveysMessages, locale);
  const count = new Intl.NumberFormat(locale);
  const organisation = me.organisations.find((candidate) => candidate.id === me.currentOrganisationId) ?? me.organisations[0];
  const canCreate = organisation !== undefined && editorRoles.includes(organisation.role);
  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");
  const [place] = useState(() => readPlace(address, storage.get(sizeKey)));
  const [page, setPage] = useState(place.page);
  const [sort, setSort] = useState<DataGridSort>(place.sort);
  const [size, setSize] = useState(place.size);
  const [list, setList] = useState<SurveyList | null>(null);
  const [failed, setFailed] = useState(false);
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    setFailed(false);

    try {
      setList(await client.list({ ...(filter !== "all" && { status: filter }), search, sort: sortToParam(sort), offset: (page - 1) * size, limit: size }));
    } catch {
      setFailed(true);
    }
  }, [client, filter, search, sort, page, size]);

  useEffect(() => {
    const timer = setTimeout(() => void load(), searchDelayMs);

    return () => clearTimeout(timer);
  }, [load, searchDelayMs]);

  const latestOnAddress = useRef(onAddress);
  latestOnAddress.current = onAddress;
  useEffect(() => latestOnAddress.current?.(writePlace(page, sort, size)), [page, sort, size]);

  const empty = list !== null && list.counts.all === 0;
  const columns: DataGridColumn<SurveyListItem>[] = [
    {
      key: "title",
      header: t("title"),
      sortable: true,
      phone: "title",
      cell: (survey) => (
        <span className="surveys__title">
          <a
            href={`/surveys/${survey.id}`}
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              onOpen(survey.id);
            }}
          >
            {survey.title}
          </a>
          <span className="surveys__muted">{t("by", { name: survey.createdBy })}</span>
        </span>
      ),
    },
    { key: "status", header: t("status"), sortable: true, cell: (survey) => statusText(t, survey) },
    { key: "languages", header: t("languages"), cell: (survey) => survey.languages.map((language) => language.toUpperCase()).join(" ") },
    {
      key: "responses",
      header: t("responses"),
      sortable: true,
      cell: (survey) =>
        survey.publishedVersion === null ? (
          "—"
        ) : onOpenResults === undefined ? (
          count.format(Number(survey.responses))
        ) : (
          <button
            type="button"
            className="surveys__count"
            aria-label={Number(survey.responses) === 1 ? t("responseOne") : t("responsesCount", { count: Number(survey.responses) })}
            onClick={(event) => {
              event.stopPropagation();
              onOpenResults(survey.id);
            }}
          >
            {count.format(Number(survey.responses))}
          </button>
        ),
    },
    { key: "changed", header: t("changed"), sortable: true, cell: (survey) => changedText(t, locale, survey.updatedAt, now(), timeZone) },
  ];
  const grid = (
    <DataGrid
      label={t("surveys")}
      columns={columns}
      rows={list?.items ?? null}
      rowKey={(survey) => survey.id}
      sort={sort}
      onSort={setSort}
      onOpen={(survey) => onOpen(survey.id)}
      paging={{
        page,
        pageSize: size,
        total: list?.total ?? 0,
        onPage: setPage,
        onPageSize: (next) => {
          setSize(next);
          storage.set(sizeKey, String(next));
        },
      }}
      empty={
        <>
          <p>{search.trim() === "" ? t("nothingFound") : t("noMatch", { search: search.trim() })}</p>
          {search.trim() !== "" && (
            <button type="button" className="surveys__button" onClick={() => changeSearch("")}>
              {t("clearSearch")}
            </button>
          )}
        </>
      }
      error={failed}
      onRetry={() => void load()}
      texts={dataGridTexts(locale)}
    />
  );

  function changeSearch(value: string) {
    setSearch(value);
    setPage(1);
  }

  return (
    <main className="surveys" lang={locale}>
      <div className="surveys__head">
        <div>
          <h1>{t("surveys")}</h1>
          <p className="surveys__of">{t("ofOrganisation", { organisation: organisation?.name ?? "" })}</p>
        </div>
        {canCreate && !empty && (
          <button type="button" className="surveys__button surveys__button--primary" onClick={() => setCreating(true)}>
            <span aria-hidden="true">+ </span>
            {t("newSurvey")}
          </button>
        )}
      </div>

      {list === null && grid}

      {empty && (
        <section className="surveys__empty" aria-labelledby="surveys-empty">
          <h2 id="surveys-empty">{t("emptyTitle")}</h2>
          {canCreate ? (
            <>
              <p className="surveys__muted">{t("emptyText")}</p>
              <button type="button" className="surveys__button surveys__button--primary" onClick={() => setCreating(true)}>
                <span aria-hidden="true">+ </span>
                {t("createFirst")}
              </button>
            </>
          ) : (
            <p className="surveys__muted">{t("emptyReadOnly", { organisation: organisation?.name ?? "" })}</p>
          )}
        </section>
      )}

      {list !== null && !empty && (
        <>
          <div className="surveys__tools">
            <input
              type="search"
              className="surveys__search"
              aria-label={t("searchSurveys")}
              placeholder={t("searchSurveys")}
              value={search}
              onChange={(event) => changeSearch(event.target.value)}
            />
            <span className="surveys__chips" role="group" aria-label={t("filters")}>
              {filters.map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  aria-pressed={filter === key}
                  onClick={() => {
                    setFilter(key);
                    setPage(1);
                  }}
                >
                  {t(label, { count: list.counts[key] })}
                </button>
              ))}
            </span>
          </div>
          {grid}
        </>
      )}

      {creating && <NewSurveyDialog client={client} locale={locale} onCancel={() => setCreating(false)} onCreated={onOpen} />}
    </main>
  );
}

function statusText(t: Translate<SurveysMessageKey>, survey: SurveyListItem): string {
  switch (survey.status) {
    case "published":
      return survey.hasChanges ? t("statusChanged", { version: survey.publishedVersion ?? 0 }) : t("statusPublished", { version: survey.publishedVersion ?? 0 });
    case "closed":
      return t("statusClosed");
    case "archived":
      return t("statusArchived");
    default:
      return t("statusDraft");
  }
}

function changedText(t: Translate<SurveysMessageKey>, locale: Locale, updatedAt: string, now: Date, timeZone: string | undefined): string {
  const zone = timeZone !== undefined ? { timeZone } : {};
  const day = (value: Date) => new Intl.DateTimeFormat("en-CA", zone).format(value);
  const changed = new Date(updatedAt);
  const yesterday = new Date(now.getTime() - 86_400_000);

  if (day(changed) === day(now)) {
    return t("today", { time: new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit", hourCycle: "h23", ...zone }).format(changed) });
  }

  if (day(changed) === day(yesterday)) {
    return t("yesterday");
  }

  return new Intl.DateTimeFormat(locale === "en" ? "en-GB" : locale, { day: "numeric", month: "short", ...zone }).format(changed);
}

const languages = [
  ["de", "languageDe"],
  ["en", "languageEn"],
] as const;

/** New survey (W6 B): internal title, languages and the default language. */
function NewSurveyDialog({ client, locale, onCancel, onCreated }: { client: SurveysClient; locale: Locale; onCancel: () => void; onCreated: (id: string) => void }) {
  const t = createTranslator(surveysMessages, locale);
  const id = useId();
  const title = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [chosen, setChosen] = useState<string[]>([locale]);
  const [defaultLanguage, setDefaultLanguage] = useState<string>(locale);
  const [error, setError] = useState<SurveysMessageKey | null>(null);
  const [busy, setBusy] = useState(false);
  // From a template (AWK-17, W10 C): the template brings its languages, so only the title is asked for.
  const [start, setStart] = useState<"blank" | "template" | "sample">("blank");
  const fromTemplate = start === "template";
  const setFromTemplate = (on: boolean) => setStart(on ? "template" : "blank");
  // Or a sample (W26): a complete survey with its own look and sample answers.
  const [samples, setSamples] = useState<SampleSurvey<ThemeTokens>[] | null>(null);
  const [sampleId, setSampleId] = useState<string | null>(null);
  const [templates, setTemplates] = useState<SurveyTemplate[] | null>(null);
  const [templateId, setTemplateId] = useState<string | null>(null);

  useEffect(() => {
    if (fromTemplate && templates === null) {
      client.listTemplates().then(setTemplates, () => setTemplates([]));
    }
  }, [client, fromTemplate, templates]);

  useEffect(() => {
    if (start === "sample" && samples === null) {
      client.listSamples<ThemeTokens>().then(setSamples, () => setSamples([]));
    }
  }, [client, start, samples]);

  useEffect(() => title.current?.focus(), []);

  function toggle(language: string) {
    const next = chosen.includes(language) ? chosen.filter((item) => item !== language) : languages.map(([code]) => code).filter((code) => code === language || chosen.includes(code));
    setChosen(next);

    if (!next.includes(defaultLanguage) && next[0] !== undefined) {
      setDefaultLanguage(next[0]);
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();

    if (start === "sample") {
      const sample = samples?.find((each) => each.id === sampleId);

      if (sample === undefined) {
        setError("chooseSample");
        return;
      }

      setBusy(true);
      client.createSample(sample.id, name.trim() || (sample.names[locale] ?? sample.names.en ?? sample.id)).then(onCreated, () => {
        setError("somethingWrong");
        setBusy(false);
      });
      return;
    }

    if (fromTemplate && templateId === null) {
      setError("chooseTemplate");
      return;
    }

    if (!fromTemplate && chosen.length === 0) {
      setError("languageRequired");
      return;
    }

    setBusy(true);

    try {
      onCreated(
        await client.create(
          fromTemplate && templateId !== null ? { title: name, languages: [], defaultLanguage: "", templateId } : { title: name, languages: chosen, defaultLanguage },
        ),
      );
    } catch (caught) {
      const code = caught instanceof ApiError ? caught.problem.errors?.title?.[0] : undefined;
      setError(code === "title-required" ? "titleRequired" : code === "title-too-long" ? "titleTooLong" : "somethingWrong");
      setBusy(false);
    }
  }

  return (
    <Dialog title={t("newSurvey")} onClose={onCancel} className={start === "sample" ? "surveys__dialog surveys__dialog--wide" : "surveys__dialog"}>
        <form className="surveys__form" onSubmit={(event) => void submit(event)} noValidate>
          <fieldset className="surveys__field">
            <legend className="surveys__label">{t("startWith")}</legend>
            <span className="surveys__checks">
              <label className="surveys__check">
                <input type="radio" name={`${id}-start`} checked={start === "blank"} onChange={() => setFromTemplate(false)} />
                {t("blank")}
              </label>
              <label className="surveys__check">
                <input type="radio" name={`${id}-start`} checked={fromTemplate} onChange={() => setFromTemplate(true)} />
                {t("fromTemplate")}
              </label>
              <label className="surveys__check">
                <input type="radio" name={`${id}-start`} checked={start === "sample"} onChange={() => setStart("sample")} />
                {t("fromSample")}
              </label>
            </span>
          </fieldset>
          {fromTemplate &&
            (templates === null ? (
              <p role="status" aria-busy="true" className="surveys__muted">
                {t("loadingTemplates")}
              </p>
            ) : (
              <div className="surveys__templates" role="radiogroup" aria-label={t("template")}>
                {templates.map((template) => {
                  const shown = template.names?.[locale] ?? template.names?.en ?? template.name ?? template.id;
                  const languagesText = template.languages.map((language) => language.toUpperCase()).join(" ");

                  return (
                    <label key={template.id} className={`surveys__template${template.id === templateId ? " surveys__template--on" : ""}`}>
                      <input type="radio" name={`${id}-template`} checked={template.id === templateId} onChange={() => setTemplateId(template.id)} />
                      <b>{shown}</b>
                      <span className="surveys__muted">
                        {template.questions === 1 ? t("templateMetaOne", { languages: languagesText }) : t("templateMeta", { count: template.questions, languages: languagesText })}
                      </span>
                    </label>
                  );
                })}
              </div>
            ))}
          {start === "sample" &&
            (samples === null ? (
              <p role="status" aria-busy="true" className="surveys__muted">
                {t("loadingSamples")}
              </p>
            ) : (
              <>
                <p className="surveys__muted">{t("samplesLead")}</p>
                <div className="surveys__samples" role="radiogroup" aria-label={t("samples")}>
                  {samples.map((sample) => {
                    const preset = themePresets.find((each) => each.id === sample.look);
                    const shown = (text: Record<string, string>) => text[locale] ?? text.en ?? "";

                    return (
                      <label key={sample.id} className={`surveys__sample${sample.id === sampleId ? " surveys__sample--on" : ""}`}>
                        <input type="radio" className="surveys__sample-radio" name={`${id}-sample`} checked={sample.id === sampleId} onChange={() => setSampleId(sample.id)} />
                        <span className="surveys__sample-thumb">
                          <LookThumb tokens={sample.tokens} question={shown(sample.preview.question)} choices={sample.preview.choices.map(shown)} />
                        </span>
                        <span className="surveys__sample-body">
                          <b>{shown(sample.names)}</b>
                          <span className="surveys__muted"> · {shown(sample.kind)} · {t("sampleLook", { look: preset?.name[locale] ?? sample.look })}</span>
                          <span className="surveys__muted">{t("sampleMeta", { questions: sample.questions, responses: sample.responses })}</span>
                          <span className="surveys__features">
                            {(sample.features[locale] ?? sample.features.en ?? []).map((feature) => (
                              <span key={feature}>{feature}</span>
                            ))}
                          </span>
                        </span>
                      </label>
                    );
                  })}
                </div>
              </>
            ))}
          <div className="surveys__field">
            <label className="surveys__label" htmlFor={`${id}-name`}>
              {t("titleLabel")}
            </label>
            <input
              id={`${id}-name`}
              ref={title}
              className="surveys__input"
              maxLength={200}
              value={name}
              aria-invalid={error === "titleRequired" || error === "titleTooLong"}
              aria-describedby={error !== null ? `${id}-error` : undefined}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          {start === "blank" && (
          <fieldset className="surveys__field">
            <legend className="surveys__label">{t("languagesLabel")}</legend>
            <span className="surveys__checks">
              {languages.map(([code, label]) => (
                <label key={code} className="surveys__check">
                  <input type="checkbox" checked={chosen.includes(code)} onChange={() => toggle(code)} />
                  {t(label)}
                  {chosen.length > 1 && code === defaultLanguage && <span className="surveys__muted"> {t("defaultMark")}</span>}
                </label>
              ))}
            </span>
          </fieldset>
          )}
          {start === "blank" && chosen.length > 1 && (
            <div className="surveys__field">
              <span className="surveys__label" id={`${id}-default-label`}>
                {t("defaultLanguage")}
              </span>
              <ComboBox
                id={`${id}-default`}
                labelledBy={`${id}-default-label`}
                options={languages.filter(([code]) => chosen.includes(code)).map(([code, label]) => ({ value: code, label: t(label) }))}
                value={defaultLanguage}
                onChange={setDefaultLanguage}
                texts={{ choose: t("defaultLanguage") }}
              />
            </div>
          )}
          {error !== null && (
            <span id={`${id}-error`} className="surveys__error">
              {t(error)}
            </span>
          )}
          <div className="surveys__actions">
            <button type="button" className="surveys__button" onClick={onCancel}>
              {t("cancel")}
            </button>
            <button type="submit" className="surveys__button surveys__button--primary" disabled={busy}>
              {t("create")}
            </button>
          </div>
        </form>
    </Dialog>
  );
}
