// SPDX-License-Identifier: AGPL-3.0-only
import "./participants.css";
import { ApiError, type AutoReminders, type ContactsClient, type ParticipantItem, type ParticipantList, type ParticipantsClient, type ParticipantSelection, type ParticipantStatus } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, DataGrid, dataGridTexts, Menu, MenuItem, MenuLink, MenuSeparator, type DataGridColumn } from "@askwerk/ui";
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { ContactsPicker } from "./ContactsPicker";
import { ImportDialog } from "./ImportDialog";
import { InviteDialog } from "./InviteDialog";
import { dayText, fieldLabel, relativeDay, whenText } from "./peopleFormat";
import { PersonPanel } from "./PersonPanel";
import { peopleMessages, type PeopleMessageKey } from "./peopleMessages";

type ParticipantsPanelProps = {
  /** After each load, e.g. so the sidebar's count follows (W35). */
  onLoaded?: () => void;
  client: ParticipantsClient;
  surveyId: string;
  locale: Locale;
  preview?: (language: string) => Promise<{ subject: string; text: string }>;
  writeClipboard?: (text: string) => Promise<void>;
  timeZone?: string;
  /** The address book (W35): From contacts, and saving people there. */
  contacts?: ContactsClient;
  /** Saves a file, e.g. the chosen people as CSV. */
  saveFile?: (name: string, file: Blob) => void;
  /** Today, for "today" and "yesterday". */
  now?: () => Date;
};

/** A page of 50, with ‹ › (W35). */
const size = 50;

/** Where each status stands in an invitation's life, for its trail: sent, opened, started, answered. */
const progress: Record<ParticipantStatus, number> = { waiting: 0, "link-only": 0, sent: 1, opened: 2, started: 3, answered: 4, bounced: -1, unsubscribed: 0 };

/** The status chips (W35): a status, or everyone who has not answered; and what the chosen are "of". */
const chips: [string | null, PeopleMessageKey, PeopleMessageKey][] = [
  [null, "all", "ofAll"],
  ["not-answered", "notAnswered", "ofNotAnswered"],
  ["started", "startedChip", "ofStarted"],
  ["answered", "answeredChip", "ofAnswered"],
  ["bounced", "bouncedChip", "ofBounced"],
  ["unsubscribed", "unsubscribedChip", "ofUnsubscribed"],
];

/**
 * Participants (AWK-7, W12 D, W35): the funnel says where people stop; search and filter by status and any field;
 * the chosen get a reminder, the invitation again, an export or a contacts group; people come by typing, a file or
 * Contacts; one person opens with their fields and history; reminders go out by themselves.
 */
export function ParticipantsPanel({ client, surveyId, locale, onLoaded, preview, writeClipboard = (text) => navigator.clipboard.writeText(text), timeZone, contacts, saveFile = download, now = () => new Date() }: ParticipantsPanelProps) {
  const t = useMemo(() => createTranslator(peopleMessages, locale), [locale]);
  const id = useId();
  const [list, setList] = useState<ParticipantList | null>(null);
  const [failed, setFailed] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const [adding, setAdding] = useState<"typed" | "file" | "contacts" | null>(null);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const [field, setField] = useState<{ key: string; value: string } | null>(null);
  const [filtering, setFiltering] = useState<{ key: string; value: string } | null>(null);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [all, setAll] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [grouping, setGrouping] = useState<string | null>(null);
  const [opened, setOpened] = useState<ParticipantItem | null>(null);
  const query = { ...(search.trim() === "" ? {} : { search }), ...(status === null ? {} : { status }), ...(field === null ? {} : { field: `${field.key}:${field.value}` }) };
  const reload = useCallback(() => {
    setFailed(false);

    return client.list(surveyId, { ...query, offset: (page - 1) * size, limit: size }).then((next) => {
      setList(next);
      onLoaded?.();
    }, () => setFailed(true));
    // query is rebuilt from these each render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, surveyId, page, search, status, field]);

  useEffect(() => {
    const timer = setTimeout(() => void reload(), 200);
    return () => clearTimeout(timer);
  }, [reload]);

  const day = (at: string) => dayText(locale, at, timeZone);
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
  const closed = (item: ParticipantItem) => item.status === "bounced" || item.status === "unsubscribed";

  /** The last activity with what it was (W35): "answered 3 Oct", "mailbox full · 2 Oct", "yesterday". */
  const activity = (item: ParticipantItem) => {
    if (item.lastActivity == null) {
      return "—";
    }

    const date = relativeDay(locale, item.lastActivity, { today: t("today"), yesterday: t("yesterday") }, now(), timeZone);
    const kinds: Partial<Record<NonNullable<ParticipantItem["lastActivityKind"]>, PeopleMessageKey>> = { answered: "activityAnswered", opened: "activityOpened", reminded: "activityReminded", sent: "activitySent", invited: "activityInvited" };
    const kind = item.lastActivityKind == null ? undefined : kinds[item.lastActivityKind];

    return item.lastActivityKind === "bounced" ? t("activityBounced", { reason: t(item.bounceReason ?? "refused"), date }) : kind === undefined ? date : t(kind, { date });
  };

  const columns: DataGridColumn<ParticipantItem>[] = [
    {
      key: "name",
      header: t("person"),
      phone: "title",
      cell: (item) => (
        <span className="participants__who">
          <b>{item.name ?? "—"}</b>
          <span>{[item.email, item.externalId].filter(Boolean).join(" · ")}</span>
        </span>
      ),
    },
    { key: "language", header: t("language"), cell: (item) => item.language?.toUpperCase() ?? "—" },
    ...(fieldColumn === undefined ? [] : [{ key: `field-${fieldColumn}`, header: fieldLabel(fieldColumn), cell: (item: ParticipantItem) => item.fields?.[fieldColumn] ?? "—" }]),
    {
      key: "status",
      header: t("status"),
      cell: (item) => (
        <span className="participants__status" data-status={item.status}>
          <span className="participants__state">
            <i className="participants__dot" aria-hidden="true" />
            {item.status === "started" && item.page != null && item.pages != null ? t("startedPage", { page: item.page, pages: item.pages }) : t(item.status)}
          </span>
          {item.status !== "unsubscribed" && (
            <span className="participants__trail" aria-hidden="true">
              {[1, 2, 3, 4].map((step) => (
                <i
                  key={step}
                  data-on={progress[item.status] >= step ? "" : undefined}
                  data-done={item.status === "answered" && step === 4 ? "" : undefined}
                  data-bad={item.status === "bounced" && step === 1 ? "" : undefined}
                />
              ))}
            </span>
          )}
        </span>
      ),
    },
    { key: "invited", header: t("invitedOn"), cell: (item) => <span className="participants__small">{item.invitedAt != null ? day(item.invitedAt) : item.sentAt != null ? day(item.sentAt) : "—"}</span> },
    { key: "activity", header: t("lastActivity"), cell: (item) => <span className="participants__small">{activity(item)}</span> },
    {
      key: "reminders",
      header: t("reminders"),
      cell: (item) => <span className="participants__small">{closed(item) ? "—" : item.reminders === 0 ? "0" : `${item.reminders}${item.remindedAt == null ? "" : ` · ${day(item.remindedAt)}`}`}</span>,
    },
    {
      key: "action",
      header: t("link"),
      hideHeader: true,
      cell: (item) =>
        item.status === "unsubscribed" ? null : item.status === "bounced" ? (
          <button
            type="button"
            className="participants__button participants__button--small"
            onClick={(event) => {
              event.stopPropagation();
              setOpened(item);
            }}
          >
            {t("fixEmail")}
          </button>
        ) : (
          <button
            type="button"
            className="participants__button participants__button--small"
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
  // What the chosen are of (W35): the status shown; with all shown, "not answered" when none of the chosen has answered.
  const chosenRows = (list?.items ?? []).filter((item) => selected.has(item.id));
  const ofStatus = status ?? (!all && chosenRows.length > 0 && chosenRows.every((item) => !["answered", "bounced", "unsubscribed"].includes(item.status)) ? "not-answered" : null);
  const [, , ofKey] = chips.find(([key]) => key === ofStatus) ?? chips[0]!;

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
            <button type="button" className="participants__button" {...props}>
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
            <button type="button" className="participants__button participants__button--primary" {...props}>
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
            {count(["bounced"]) > 0 && (
              <span>
                <b>{count(["bounced"])}</b> {t("sideBounced")}
              </span>
            )}
            {count(["unsubscribed"]) > 0 && (
              <span>
                <b>{count(["unsubscribed"])}</b> {t("sideUnsubscribed")}
              </span>
            )}
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
            <button type="button" className="participants__filter" aria-label={t("removeFilter", { filter: `${fieldLabel(field.key)}: ${field.value}` })} onClick={() => setField(null)}>
              {fieldLabel(field.key)}: {field.value} ✕
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
              <ComboBox labelledBy={`${id}-field`} options={(list.fieldKeys ?? []).map((key) => ({ value: key, label: fieldLabel(key) }))} value={filtering.key} onChange={(key) => setFiltering({ ...filtering, key })} texts={{ choose: t("filterField") }} />
              <input className="designer-input" aria-label={t("filterValue")} placeholder={t("filterValue")} value={filtering.value} onChange={(event) => setFiltering({ ...filtering, value: event.target.value })} />
              <button
                type="button"
                className="participants__button"
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
          onOpen={setOpened}
          paging={{
            page,
            pageSize: size,
            total: matching,
            compact: true,
            ...(field === null ? {} : { of: total, note: t("fieldCount", { field: fieldLabel(field.key), count: matching }) }),
            onPage: setPage,
            onPageSize: () => undefined,
          }}
          selection={{
            selected,
            all,
            boxed: true,
            count: (chosenOnes) => t("chosen", { count: chosenOnes }),
            context: t(ofKey, { count: chipCount(ofStatus) }),
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
        <ReminderBar t={t} id={id} settings={list.autoReminders} when={(at) => whenText(locale, at, timeZone)} onChange={(on, days, max) => client.setAutoReminders(surveyId, on, days, max).then(() => reload(), failure)} />
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
          knownFields={list?.fieldKeys ?? []}
          read={(source) => client.table(surveyId, source)}
          check={(people) => client.checkPeople(surveyId, people)}
          onImport={(people, choice) =>
            client
              .invite(surveyId, { participants: people, send: choice.sendNow, saveToContacts: choice.saveToContacts, importedFrom: choice.fileName ?? "", ...(choice.group === "" ? {} : { contactGroup: choice.group }) })
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
          status={opened.status}
          statusText={opened.status === "started" && opened.page != null && opened.pages != null ? t("startedPage", { page: opened.page, pages: opened.pages }) : t(opened.status)}
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

/**
 * Automatic reminders (W35), as one sentence: "Automatic reminders · after 7 days ▾ to people who have not answered,
 * at most 2 ▾ · next: 23 Oct, 09:00 to 392 people", the switch before it and the reminder text after it.
 */
function ReminderBar({ t, id, settings, when, onChange }: { t: ReturnType<typeof createTranslator<PeopleMessageKey>>; id: string; settings: AutoReminders; when: (at: string) => string; onChange: (on: boolean, days: number, max: number) => void }) {
  return (
    <div className="participants__reminders">
      <button type="button" role="switch" aria-checked={settings.on} aria-labelledby={`${id}-auto`} className="participants__switch" onClick={() => onChange(!settings.on, settings.days, settings.max)} />
      <span className="participants__sentence">
        <b id={`${id}-auto`}>{t("autoReminders")}</b>
        {" · "}
        <ComboBox
          labelledBy={`${id}-auto`}
          options={[3, 5, 7, 10, 14].map((days) => ({ value: String(days), label: t("autoDays", { days }) }))}
          value={String(settings.days)}
          onChange={(days) => onChange(settings.on, Number(days), settings.max)}
          texts={{ choose: t("autoDays", { days: settings.days }) }}
        />{" "}
        <span id={`${id}-most`}>{t("autoMost")}</span>{" "}
        <span id={`${id}-max`} className="look-visually-hidden">
          {t("autoMax")}
        </span>
        <ComboBox
          labelledBy={`${id}-max`}
          options={[1, 2, 3, 4, 5].map((max) => ({ value: String(max), label: String(max) }))}
          value={String(settings.max)}
          onChange={(max) => onChange(settings.on, settings.days, Number(max))}
          texts={{ choose: String(settings.max) }}
        />
        {settings.on && (
          <>
            {" · "}
            <span className="participants__small">
              {settings.nextAt === null ? t("autoNone") : settings.nextCount === 1 ? t("autoNextOne", { when: when(settings.nextAt) }) : t("autoNext", { when: when(settings.nextAt), count: settings.nextCount })}
            </span>
          </>
        )}
      </span>
      <a className="participants__button participants__button--small" href="/messages">
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
