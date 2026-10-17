// SPDX-License-Identifier: AGPL-3.0-only
import "./participants.css";
import { ApiError, type AutoReminders, type ContactsClient, type ParticipantItem, type ParticipantList, type ParticipantsClient, type ParticipantSelection, type ParticipantStatus } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, DataGrid, dataGridTexts, Menu, MenuItem, MenuLink, MenuSeparator, pageSizes, sortToParam, type DataGridColumn, type DataGridSort } from "@askwerk/ui";
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { browserStorage, type PreferenceStorage } from "../account/AccountApp";
import { ContactsPicker } from "./ContactsPicker";
import { ImportDialog } from "./ImportDialog";
import { InviteDialog } from "./InviteDialog";
import { PersonPanel } from "./PersonPanel";
import { peopleMessages, type PeopleMessageKey } from "./peopleMessages";

type ParticipantsPanelProps = {
  client: ParticipantsClient;
  surveyId: string;
  locale: Locale;
  preview?: (language: string) => Promise<{ subject: string; text: string }>;
  writeClipboard?: (text: string) => Promise<void>;
  timeZone?: string;
  /** Remembers the page size on this device. */
  storage?: PreferenceStorage;
  /** The address book (W35): From contacts, and saving people there. */
  contacts?: ContactsClient;
  /** Saves a file, e.g. the chosen people as CSV. */
  saveFile?: (name: string, file: Blob) => void;
};

const sizeKey = "askwerk.grid.participants.size";

/** Where each status stands in an invitation's life, for its trail: sent, opened, started, answered. */
const progress: Record<ParticipantStatus, number> = { waiting: 0, "link-only": 0, sent: 1, opened: 2, started: 3, answered: 4, bounced: -1, unsubscribed: 0 };

/** The status chips (W35): a status, or everyone who has not answered. */
const chips: [string | null, PeopleMessageKey][] = [
  [null, "all"],
  ["not-answered", "notAnswered"],
  ["started", "startedChip"],
  ["answered", "answeredChip"],
  ["bounced", "bouncedChip"],
  ["unsubscribed", "unsubscribedChip"],
];

/**
 * Participants (AWK-7, W12 D, W35): the funnel says where people stop; search and filter by status and any field;
 * the chosen get a reminder, the invitation again, an export or a contacts group; people come by typing, a file or
 * Contacts; one person opens with their fields and history; reminders go out by themselves.
 */
export function ParticipantsPanel({ client, surveyId, locale, preview, writeClipboard = (text) => navigator.clipboard.writeText(text), timeZone, storage = browserStorage, contacts, saveFile = download }: ParticipantsPanelProps) {
  const t = useMemo(() => createTranslator(peopleMessages, locale), [locale]);
  const id = useId();
  const [list, setList] = useState<ParticipantList | null>(null);
  const [failed, setFailed] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const [adding, setAdding] = useState<"typed" | "file" | "contacts" | null>(null);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(() => [Number(storage.get(sizeKey))].find((value) => (pageSizes as readonly number[]).includes(value)) ?? 50);
  const [sort, setSort] = useState<DataGridSort | undefined>(undefined);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const [field, setField] = useState<{ key: string; value: string } | null>(null);
  const [filtering, setFiltering] = useState<{ key: string; value: string } | null>(null);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [all, setAll] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [grouping, setGrouping] = useState<string | null>(null);
  const [opened, setOpened] = useState<ParticipantItem | null>(null);
  const query = { ...(sort === undefined ? {} : { sort: sortToParam(sort) }), ...(search.trim() === "" ? {} : { search }), ...(status === null ? {} : { status }), ...(field === null ? {} : { field: `${field.key}:${field.value}` }) };
  const reload = useCallback(() => {
    setFailed(false);

    return client.list(surveyId, { ...query, offset: (page - 1) * size, limit: size }).then(setList, () => setFailed(true));
    // query is rebuilt from these each render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, surveyId, sort, page, size, search, status, field]);

  useEffect(() => {
    const timer = setTimeout(() => void reload(), 200);
    return () => clearTimeout(timer);
  }, [reload]);

  const dates = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", ...(timeZone === undefined ? {} : { timeZone }) });
  const matching = list?.matching ?? list?.total ?? 0;
  const chosenCount = all ? matching : selected.size;
  const chosen = (): ParticipantSelection => (all ? { all: true } : { participantIds: [...selected] });
  const clearChoice = () => {
    setSelected(new Set());
    setAll(false);
    setConfirming(false);
    setGrouping(null);
  };
  const failure = (error: unknown) => setNotice(error instanceof ApiError && error.problem.code === "email-not-set-up" ? t("noEmail") : t("failed"));
  const act = (action: Promise<unknown>, done: string, refresh = true) => {
    setNotice(null);
    action.then(() => {
      setNotice(done);
      clearChoice();
      refresh && void reload();
    }, failure);
  };
  const fieldColumn = field?.key ?? list?.fieldKeys?.[0];

  const columns: DataGridColumn<ParticipantItem>[] = [
    {
      key: "name",
      header: t("person"),
      sortable: true,
      phone: "title",
      cell: (item) => (
        <span className="participants__who">
          <b>{item.name ?? "—"}</b>
          <span>{[item.email, item.externalId].filter(Boolean).join(" · ")}</span>
        </span>
      ),
    },
    { key: "language", header: t("language"), cell: (item) => item.language?.toUpperCase() ?? "—" },
    ...(fieldColumn === undefined ? [] : [{ key: `field-${fieldColumn}`, header: fieldColumn, cell: (item: ParticipantItem) => item.fields?.[fieldColumn] ?? "—" }]),
    {
      key: "status",
      header: t("status"),
      sortable: true,
      cell: (item) => (
        <span className="participants__status" data-status={item.status}>
          <span>{t(item.status)}</span>
          <span className="participants__trail" aria-hidden="true">
            {[1, 2, 3, 4].map((step) => (
              <i key={step} data-on={progress[item.status] >= step ? "" : undefined} data-bad={item.status === "bounced" && step === 1 ? "" : undefined} />
            ))}
          </span>
        </span>
      ),
    },
    { key: "sent", header: t("invitedOn"), sortable: true, cell: (item) => (item.sentAt === null ? "—" : dates.format(new Date(item.sentAt))) },
    { key: "activity", header: t("lastActivity"), cell: (item) => (item.lastActivity == null ? "—" : dates.format(new Date(item.lastActivity))) },
    { key: "reminders", header: t("reminders"), align: "end", cell: (item) => (item.reminders === 0 ? "0" : `${item.reminders}${item.remindedAt == null ? "" : ` · ${dates.format(new Date(item.remindedAt))}`}`) },
    {
      key: "link",
      header: t("link"),
      cell: (item) => (
        <button
          type="button"
          className="results-open participants__copy"
          aria-label={t("copyLinkOf", { email: item.email })}
          onClick={(event) => {
            event.stopPropagation();
            void writeClipboard(item.link).then(() => setCopied(item.id), () => undefined);
          }}
        >
          {copied === item.id ? t("copied") : t("copyLink")}
        </button>
      ),
    },
  ];

  const counts = list?.counts ?? {};
  const count = (statuses: ParticipantStatus[]) => statuses.reduce((sum, each) => sum + (counts[each] ?? 0), 0);
  const total = list?.total ?? 0;
  const share = (part: number) => (total === 0 ? 0 : Math.round((part / total) * 100));
  const funnel = [
    { label: t("funnelInvited"), value: total, width: 100 },
    { label: t("funnelSent", { share: share(count(["sent", "opened", "started", "answered"])) }), value: count(["sent", "opened", "started", "answered"]) },
    { label: t("funnelOpened", { share: share(count(["opened", "started", "answered"])) }), value: count(["opened", "started", "answered"]) },
    { label: t("funnelStarted", { share: share(count(["started", "answered"])) }), value: count(["started", "answered"]) },
    { label: t("funnelAnswered", { share: share(count(["answered"])) }), value: count(["answered"]), last: true },
  ];
  const chipCount = (key: string | null) => (key === null ? total : key === "not-answered" ? total - count(["answered", "bounced", "unsubscribed"]) : (counts[key as ParticipantStatus] ?? 0));

  return (
    <div className="participants">
      <div className="participants__head">
        <div>
          <h2 className="participants__title">{t("title")}</h2>
          <p className="participants__lead">{t("lead")}</p>
        </div>
        <span className="participants__grow" />
        <Menu
          label={t("export")}
          trigger={(props) => (
            <button type="button" className="designer-button" {...props}>
              {t("export")} ▾
            </button>
          )}
        >
          <MenuLink href={client.exportUrl(surveyId)}>{t("exportAll")}</MenuLink>
          <MenuLink href={client.exportUrl(surveyId, query)}>{t("exportShown")}</MenuLink>
          <MenuSeparator />
          <MenuLink href={client.linksUrl(surveyId)}>{t("exportLinks")}</MenuLink>
        </Menu>
        <Menu
          label={t("addPeople")}
          trigger={(props) => (
            <button type="button" className="designer-button designer-button--primary" {...props}>
              {t("addPeople")} ▾
            </button>
          )}
        >
          <MenuItem onSelect={() => setAdding("typed")} hint={t("addTypedHint")}>
            {t("addTyped")}
          </MenuItem>
          <MenuItem onSelect={() => setAdding("file")} hint={t("addFileHint")}>
            {t("addFile")}
          </MenuItem>
          {contacts !== undefined && (
            <MenuItem onSelect={() => setAdding("contacts")} hint={t("addContactsHint")}>
              {t("addContacts")}
            </MenuItem>
          )}
        </Menu>
      </div>

      {list !== null && total > 0 && (
        <div className="participants__funnel">
          {funnel.map((step, index) => (
            <div key={index} className="participants__step" data-last={step.last ? "" : undefined}>
              <b>{step.value}</b>
              <span>{step.label}</span>
              <i aria-hidden="true">
                <i style={{ width: `${total === 0 ? 0 : (step.value / total) * 100}%` }} />
              </i>
            </div>
          ))}
          <div className="participants__side">
            {count(["bounced"]) > 0 && <span data-bad="">{t("bouncedCount", { count: count(["bounced"]) })}</span>}
            {count(["unsubscribed"]) > 0 && <span data-bad="">{t("unsubscribedCount", { count: count(["unsubscribed"]) })}</span>}
            {count(["waiting"]) > 0 && <span>{t("waitingCount", { count: count(["waiting"]) })}</span>}
          </div>
        </div>
      )}

      {list !== null && total > 0 && (
        <div className="participants__tools">
          <input
            type="search"
            className="participants__search"
            aria-label={t("search")}
            placeholder={t("search")}
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
          />
          <span className="participants__chips" role="group" aria-label={t("statuses")}>
            {chips.map(([key, label]) => (
              <button
                key={label}
                type="button"
                aria-pressed={status === key}
                onClick={() => {
                  setStatus(key);
                  setPage(1);
                }}
              >
                {t(label, { count: chipCount(key) })}
              </button>
            ))}
          </span>
          <span className="participants__grow" />
          {field !== null && (
            <button type="button" className="participants__filter" aria-label={t("removeFilter", { filter: `${field.key}: ${field.value}` })} onClick={() => setField(null)}>
              {field.key}: {field.value} ✕
            </button>
          )}
          {(list.fieldKeys?.length ?? 0) > 0 && filtering === null && (
            <button type="button" className="participants__filter" onClick={() => setFiltering({ key: list.fieldKeys![0]!, value: "" })}>
              {t("addFilter")}
            </button>
          )}
          {filtering !== null && (
            <span className="participants__filtering">
              <span id={`${id}-field`} className="look-visually-hidden">
                {t("filterField")}
              </span>
              <ComboBox labelledBy={`${id}-field`} options={(list.fieldKeys ?? []).map((key) => ({ value: key, label: key }))} value={filtering.key} onChange={(key) => setFiltering({ ...filtering, key })} texts={{ choose: t("filterField") }} />
              <input className="designer-input" aria-label={t("filterValue")} placeholder={t("filterValue")} value={filtering.value} onChange={(event) => setFiltering({ ...filtering, value: event.target.value })} />
              <button
                type="button"
                className="designer-button"
                disabled={filtering.value.trim() === ""}
                onClick={() => {
                  setField({ key: filtering.key, value: filtering.value.trim() });
                  setFiltering(null);
                  setPage(1);
                }}
              >
                {t("apply")}
              </button>
            </span>
          )}
        </div>
      )}

      {notice !== null && (
        <p role="status" className="participants__notice">
          {notice}
        </p>
      )}
      {list !== null && total === 0 && <p className="participants__muted">{t("none")}</p>}
      {(list === null || total > 0) && (
        <DataGrid
          label={t("title")}
          columns={columns}
          rows={list?.items ?? null}
          rowKey={(item) => item.id}
          sort={sort}
          onSort={setSort}
          onOpen={setOpened}
          paging={{
            page,
            pageSize: size,
            total: matching,
            onPage: setPage,
            onPageSize: (next) => {
              setSize(next);
              storage.set(sizeKey, String(next));
            },
          }}
          selection={{
            selected,
            all,
            rowLabel: (item) => item.email,
            onChange: (next, nextAll) => {
              setSelected(next);
              setAll(nextAll);
              setConfirming(false);
              setGrouping(null);
            },
            actions: confirming ? (
              <>
                <span>{chosenCount === 1 ? t("removeAskOne") : t("removeAsk", { count: chosenCount })}</span>
                <button type="button" onClick={() => client.remove(surveyId, chosen()).then(({ removed, kept }) => act(Promise.resolve(), kept === 0 ? t("removed", { removed }) : t("removedKept", { removed, kept })), failure)}>
                  {t("removeYes")}
                </button>
                <button type="button" onClick={() => setConfirming(false)}>
                  {t("keep")}
                </button>
              </>
            ) : grouping !== null ? (
              <>
                <input className="participants__group" aria-label={t("groupName")} placeholder={t("groupName")} value={grouping} onChange={(event) => setGrouping(event.target.value)} />
                <button type="button" onClick={() => client.saveToContacts(surveyId, chosen(), grouping.trim() || null).then(({ added, updated }) => act(Promise.resolve(), t("savedToContacts", { added, updated })), failure)}>
                  {t("save")}
                </button>
                <button type="button" onClick={() => setGrouping(null)}>
                  {t("cancel")}
                </button>
              </>
            ) : (
              <>
                <button type="button" onClick={() => act(client.remind(surveyId, all ? undefined : chosen()), t("reminded", { count: chosenCount }), false)}>
                  {t("remindChosen")}
                </button>
                <button type="button" onClick={() => act(client.resend(surveyId, chosen()), t("resent", { count: chosenCount }))}>
                  {t("resendChosen")}
                </button>
                <button type="button" onClick={() => void client.exportChosen(surveyId, chosen()).then(({ blob, fileName }) => saveFile(fileName, blob), failure)}>
                  {t("exportChosen")}
                </button>
                <button type="button" onClick={() => setGrouping("")}>
                  {t("toContacts")}
                </button>
                <button type="button" onClick={() => setConfirming(true)}>
                  {t("remove")}
                </button>
              </>
            ),
          }}
          error={failed}
          onRetry={() => void reload()}
          texts={dataGridTexts(locale)}
        />
      )}

      {list?.autoReminders != null && (
        <ReminderBar
          t={t}
          id={id}
          settings={list.autoReminders}
          dates={new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", ...(timeZone === undefined ? {} : { timeZone }) })}
          onChange={(on, days, max) => client.setAutoReminders(surveyId, on, days, max).then(() => reload(), failure)}
        />
      )}

      {adding === "typed" && (
        <InviteDialog
          client={client}
          surveyId={surveyId}
          locale={locale}
          {...(preview === undefined ? {} : { preview })}
          onClose={() => {
            setAdding(null);
            void reload();
          }}
        />
      )}
      {adding === "file" && (
        <ImportDialog
          locale={locale}
          mode="participants"
          read={(source) => client.table(surveyId, source)}
          check={(people) => client.checkPeople(surveyId, people)}
          onImport={(people, choice) =>
            client
              .invite(surveyId, { participants: people, send: choice.sendNow, saveToContacts: choice.saveToContacts, ...(choice.group === "" ? {} : { contactGroup: choice.group }) })
              .then(({ invited }) => t("imported", { count: invited }))
          }
          onClose={(done) => {
            setAdding(null);
            done !== undefined && setNotice(done);
            void reload();
          }}
        />
      )}
      {adding === "contacts" && contacts !== undefined && (
        <ContactsPicker
          contacts={contacts}
          locale={locale}
          onInvite={(contactIds) => client.invite(surveyId, { participants: [], contactIds }).then(({ invited }) => t("imported", { count: invited }))}
          onClose={(done) => {
            setAdding(null);
            done !== undefined && setNotice(done);
            void reload();
          }}
        />
      )}
      {opened !== null && (
        <PersonPanel
          client={client}
          surveyId={surveyId}
          participantId={opened.id}
          locale={locale}
          {...(timeZone === undefined ? {} : { timeZone })}
          statusText={t(opened.status)}
          writeClipboard={writeClipboard}
          onClose={(changed) => {
            setOpened(null);
            changed && void reload();
          }}
          onRemove={() => {
            const person = opened;
            setOpened(null);
            client.remove(surveyId, { participantIds: [person.id] }).then(({ removed, kept }) => act(Promise.resolve(), kept === 0 ? t("removed", { removed }) : t("removedKept", { removed, kept })), failure);
          }}
        />
      )}
    </div>
  );
}

/** Automatic reminders (W35): on or off, after how many days, at most how many, and the next one. */
function ReminderBar({
  t,
  id,
  settings,
  dates,
  onChange,
}: {
  t: ReturnType<typeof createTranslator<PeopleMessageKey>>;
  id: string;
  settings: AutoReminders;
  dates: Intl.DateTimeFormat;
  onChange: (on: boolean, days: number, max: number) => void;
}) {
  return (
    <div className="participants__reminders">
      <button type="button" role="switch" aria-checked={settings.on} className="aw-switch" onClick={() => onChange(!settings.on, settings.days, settings.max)}>
        <b aria-hidden="true" />
        {t("autoReminders")}
      </button>
      <span id={`${id}-after`}>{t("autoAfter")}</span>
      <ComboBox
        labelledBy={`${id}-after`}
        options={[3, 5, 7, 10, 14].map((days) => ({ value: String(days), label: t("autoDays", { days }) }))}
        value={String(settings.days)}
        onChange={(days) => onChange(settings.on, Number(days), settings.max)}
        texts={{ choose: t("autoDays", { days: settings.days }) }}
      />
      <span id={`${id}-most`}>{t("autoMost")}</span>
      <ComboBox
        labelledBy={`${id}-most`}
        options={[1, 2, 3, 4, 5].map((max) => ({ value: String(max), label: String(max) }))}
        value={String(settings.max)}
        onChange={(max) => onChange(settings.on, settings.days, Number(max))}
        texts={{ choose: String(settings.max) }}
      />
      {settings.on && <span className="participants__muted">{settings.nextAt === null ? t("autoNone") : t("autoNext", { when: dates.format(new Date(settings.nextAt)), count: settings.nextCount })}</span>}
      <span className="participants__grow" />
      <a className="designer-link-button" href="/messages">
        {t("editReminderText")}
      </a>
    </div>
  );
}

/** Saves a file through a temporary link, as a download. */
function download(name: string, file: Blob) {
  const url = URL.createObjectURL(file);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
