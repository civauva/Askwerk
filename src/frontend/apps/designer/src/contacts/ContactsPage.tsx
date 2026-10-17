// SPDX-License-Identifier: AGPL-3.0-only
import "../participants/participants.css";
import "./contacts.css";
import { ApiError, type ContactItem, type ContactList, type ContactsClient, type ParticipantsClient, type SurveyListItem, type SurveysClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, DataGrid, dataGridTexts, Dialog, DialogActions, type DataGridColumn } from "@askwerk/ui";
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { ImportDialog } from "../participants/ImportDialog";
import { dayText, fieldLabel } from "../participants/peopleFormat";
import { peopleMessages, type PeopleMessageKey } from "../participants/peopleMessages";

type ContactsPageProps = {
  client: ContactsClient;
  /** Invites the chosen contacts to a survey. */
  participants: Pick<ParticipantsClient, "invite">;
  surveys: Pick<SurveysClient, "list">;
  locale: Locale;
  canEdit: boolean;
  /** How many contacts there are, for the count in the sidebar (W35). */
  onCount?: (count: number) => void;
  /** Saves a file, e.g. the chosen contacts as CSV. */
  saveFile?: (name: string, file: Blob) => void;
};

const states: [string | null, PeopleMessageKey, "all" | "invitable" | "unsubscribed" | "bounced"][] = [
  [null, "allChip", "all"],
  ["invitable", "canBeInvited", "invitable"],
  ["unsubscribed", "contactsUnsubscribed", "unsubscribed"],
  ["bounced", "contactsBounced", "bounced"],
];

/**
 * Contacts (W35): the organisation's address book. Groups at the side; search and who can be invited; the chosen go
 * into a survey, a group, a CSV, or away; each contact opens to change, with their External ID and fields.
 */
export function ContactsPage({ client, participants, surveys, locale, canEdit, onCount, saveFile = download }: ContactsPageProps) {
  const t = useMemo(() => createTranslator(peopleMessages, locale), [locale]);
  const [list, setList] = useState<ContactList | null>(null);
  const [failed, setFailed] = useState(false);
  const [group, setGroup] = useState<string | null>(null);
  const [state, setState] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [notice, setNotice] = useState<string | null>(null);
  const [editing, setEditing] = useState<ContactItem | "new" | null>(null);
  const [importing, setImporting] = useState(false);
  const [inviting, setInviting] = useState(false);
  const [grouping, setGrouping] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [naming, setNaming] = useState<string | null>(null);
  const [newGroups, setNewGroups] = useState<string[]>([]);
  const size = 50;

  const reload = useCallback(() => {
    setFailed(false);
    return client.list({ ...(group === null ? {} : { group }), ...(state === null ? {} : { state }), search, offset: (page - 1) * size, limit: size }).then(setList, () => setFailed(true));
  }, [client, group, state, search, page]);

  useEffect(() => {
    const timer = setTimeout(() => void reload(), 150);
    return () => clearTimeout(timer);
  }, [reload]);

  useEffect(() => {
    list !== null && onCount?.(list.counts.all);
  }, [list, onCount]);

  const day = (at: string) => dayText(locale, at);
  // A new group exists once someone is in it; until then it waits here, chosen, for Add to group.
  const groups = [...(list?.groups ?? []), ...newGroups.filter((name) => !(list?.groups ?? []).some((each) => each.name === name)).map((name) => ({ name, count: 0 }))];
  const ids = [...selected];
  const done = (text: string) => {
    setNotice(text);
    setSelected(new Set());
    setGrouping(null);
    setConfirming(false);
    void reload();
  };
  const fieldColumn = list?.fieldKeys[0];
  const columns: DataGridColumn<ContactItem>[] = [
    {
      key: "name",
      header: t("person"),
      phone: "title",
      cell: (contact) => (
        <span className="participants__who">
          <b>{contact.name ?? "—"}</b>
          <span>{contact.email}</span>
        </span>
      ),
    },
    { key: "external", header: t("externalId"), cell: (contact) => <span className="contacts__mono">{contact.externalId ?? "—"}</span> },
    { key: "language", header: t("language"), cell: (contact) => contact.language?.toUpperCase() ?? "—" },
    {
      key: "groups",
      header: t("groups"),
      cell: (contact) => (
        <span className="contacts__groups">
          {contact.groups.map((each) => (
            <span key={each} className="contacts__tag">
              {each}
            </span>
          ))}
        </span>
      ),
    },
    ...(fieldColumn === undefined ? [] : [{ key: `field-${fieldColumn}`, header: fieldLabel(fieldColumn), cell: (contact: ContactItem) => contact.fields[fieldColumn] ?? "—" }]),
    { key: "surveys", header: t("surveys"), cell: (contact) => <span className="participants__small">{t("surveysCount", { count: contact.surveys, answered: contact.answered })}</span> },
    { key: "last", header: t("lastInvited"), cell: (contact) => <span className="participants__small">{contact.lastInvitedAt === null ? "—" : day(contact.lastInvitedAt)}</span> },
    {
      key: "invitable",
      header: t("invitable"),
      cell: (contact) => (
        <span data-state={contact.unsubscribed ? "no" : contact.bounced ? "check" : "yes"} className="contacts__state">
          {contact.unsubscribed ? (contact.unsubscribedAt != null ? t("noUnsubscribedOn", { date: day(contact.unsubscribedAt) }) : t("noUnsubscribed")) : contact.bounced ? t("checkBounced") : t("yes")}
        </span>
      ),
    },
  ];

  return (
    <main className="admin admin--wide contacts" lang={locale}>
      <div className="participants__head">
        <div>
          <h1 className="contacts__title">{t("contacts")}</h1>
          <p className="participants__lead">{t("contactsLead")}</p>
        </div>
        <span className="participants__grow" />
        <a className="participants__button" href={client.exportUrl(group ?? undefined)} download>
          {t("export")}
        </a>
        {canEdit && (
          <>
            <button type="button" className="participants__button" onClick={() => setImporting(true)}>
              {t("import")}
            </button>
            <button type="button" className="participants__button participants__button--primary" onClick={() => setEditing("new")}>
              {t("addContact")}
            </button>
          </>
        )}
      </div>

      <div className="contacts__work">
        <nav className="contacts__groups-nav" aria-label={t("groups")}>
          <p className="import-map__title">{t("groups")}</p>
          <button type="button" aria-current={group === null ? "true" : undefined} onClick={() => (setGroup(null), setPage(1))}>
            <span>{t("allContacts")}</span>
            <small>{list?.counts.all ?? ""}</small>
          </button>
          {groups.map((each) => (
            <button key={each.name} type="button" aria-current={group === each.name ? "true" : undefined} onClick={() => (setGroup(each.name), setPage(1))}>
              <span>{each.name}</span>
              <small>{each.count}</small>
            </button>
          ))}
          {canEdit &&
            (naming === null ? (
              <button type="button" className="contacts__new-group" onClick={() => setNaming("")}>
                {t("newGroup")}
              </button>
            ) : (
              <form
                className="contacts__naming"
                onSubmit={(event) => {
                  event.preventDefault();
                  const name = naming.trim();

                  if (name !== "") {
                    setNewGroups((current) => [...current, name]);
                    setGroup(name);
                    setPage(1);
                  }

                  setNaming(null);
                }}
              >
                <input className="designer-input" aria-label={t("groupName")} placeholder={t("groupName")} value={naming} autoFocus onChange={(event) => setNaming(event.target.value)} onBlur={(event) => event.currentTarget.form?.requestSubmit()} />
              </form>
            ))}
          <p className="import-map__title">{t("fields")}</p>
          <span className="contacts__field">{t("externalId")}</span>
          {(list?.fieldKeys ?? []).map((key) => (
            <span key={key} className="contacts__field">
              {fieldLabel(key)}
            </span>
          ))}
        </nav>

        <div className="contacts__list">
          <div className="participants__tools">
            <input type="search" className="participants__search" aria-label={t("searchContacts")} placeholder={t("searchContacts")} value={search} onChange={(event) => (setSearch(event.target.value), setPage(1))} />
            <span className="participants__chips" role="group" aria-label={t("invitable")}>
              {states.map(([key, label, counted]) => (
                <button key={label} type="button" aria-pressed={state === key} onClick={() => (setState(key), setPage(1))}>
                  {t(label, { count: list?.counts[counted] ?? 0 })}
                </button>
              ))}
            </span>
          </div>
          {notice !== null && (
            <p role="status" className="participants__notice">
              {notice}
            </p>
          )}
          {list !== null && list.counts.all === 0 ? (
            <p className="participants__muted">{t("noContacts")}</p>
          ) : (
            <DataGrid
              label={t("contacts")}
              columns={columns}
              rows={list?.items ?? null}
              rowKey={(contact) => contact.id}
              {...(canEdit ? { onOpen: (contact: ContactItem) => setEditing(contact) } : {})}
              {...((list?.matching ?? 0) > size ? { paging: { page, pageSize: size, total: list?.matching ?? 0, compact: true, onPage: setPage, onPageSize: () => undefined } } : {})}
              {...(canEdit
                ? {
                    selection: {
                      selected,
                      all: false,
                      boxed: true,
                      count: (count: number) => t("chosen", { count }),
                      ...(group === null ? {} : { context: t("ofGroup", { group }) }),
                      rowLabel: (contact: ContactItem) => contact.email,
                      onChange: (next: ReadonlySet<string>) => {
                        setSelected(next);
                        setGrouping(null);
                        setConfirming(false);
                      },
                      actions: confirming ? (
                        <>
                          <span>{t("contactRemoveAsk", { count: ids.length })}</span>
                          <button type="button" onClick={() => void client.remove(ids).then(({ removed }) => done(t("contactsRemoved", { count: removed })), () => setNotice(t("failed")))}>
                            {t("removeYes")}
                          </button>
                          <button type="button" onClick={() => setConfirming(false)}>
                            {t("keep")}
                          </button>
                        </>
                      ) : grouping !== null ? (
                        <>
                          <input className="participants__group" aria-label={t("groupName")} placeholder={t("groupName")} value={grouping} autoFocus onChange={(event) => setGrouping(event.target.value)} />
                          <button type="button" disabled={grouping.trim() === ""} onClick={() => void client.group(ids, grouping.trim()).then(({ changed }) => done(t("groupAdded", { count: changed, group: grouping.trim() })), () => setNotice(t("failed")))}>
                            {t("save")}
                          </button>
                          <button type="button" onClick={() => setGrouping(null)}>
                            {t("cancel")}
                          </button>
                        </>
                      ) : (
                        <>
                          <button type="button" onClick={() => setInviting(true)}>
                            {t("inviteToSurvey")}
                          </button>
                          <button type="button" onClick={() => setGrouping(group !== null && !(list?.groups ?? []).some((each) => each.name === group) ? group : "")}>
                            {t("addToGroup")}
                          </button>
                          <button type="button" onClick={() => void client.exportChosen(ids).then(({ blob, fileName }) => saveFile(fileName, blob), () => setNotice(t("failed")))}>
                            {t("export")}
                          </button>
                          <button type="button" onClick={() => setConfirming(true)}>
                            {t("remove")}
                          </button>
                        </>
                      ),
                    },
                  }
                : {})}
              error={failed}
              onRetry={() => void reload()}
              texts={dataGridTexts(locale)}
            />
          )}
        </div>
      </div>

      {editing !== null && (
        <ContactDialog
          t={t}
          contact={editing === "new" ? null : editing}
          onSave={(input) => (editing === "new" ? client.create(input).then(() => undefined) : client.change(editing.id, input))}
          onClose={(saved) => {
            setEditing(null);
            saved && done(t("changed"));
          }}
        />
      )}
      {importing && (
        <ImportDialog
          locale={locale}
          mode="contacts"
          read={(source) => client.table(source)}
          onImport={(people, choice) => client.import(people, choice.group || null).then(({ added, updated }) => t("importedContacts", { added, updated }))}
          onClose={(text) => {
            setImporting(false);
            text !== undefined && done(text);
          }}
        />
      )}
      {inviting && (
        <InviteToSurvey
          t={t}
          surveys={surveys}
          count={ids.length}
          onInvite={(survey) => participants.invite(survey.id, { participants: [], contactIds: ids }).then(({ invited }) => t("invitedContacts", { count: invited, survey: survey.title }))}
          onClose={(text) => {
            setInviting(false);
            text !== undefined && done(text);
          }}
        />
      )}
    </main>
  );
}

/** Adds or changes a contact: email, name, language, External ID, fields and groups. */
function ContactDialog({ t, contact, onSave, onClose }: { t: (key: PeopleMessageKey, values?: Record<string, string | number>) => string; contact: ContactItem | null; onSave: (input: { email: string; name: string | null; language: string | null; externalId: string | null; fields: Record<string, string>; groups: string[] }) => Promise<void>; onClose: (saved: boolean) => void }) {
  const id = useId();
  const [email, setEmail] = useState(contact?.email ?? "");
  const [name, setName] = useState(contact?.name ?? "");
  const [language, setLanguage] = useState(contact?.language ?? "");
  const [externalId, setExternalId] = useState(contact?.externalId ?? "");
  const [groups, setGroups] = useState((contact?.groups ?? []).join(", "));
  const [fields, setFields] = useState<[string, string][]>(Object.entries(contact?.fields ?? {}));
  const [error, setError] = useState<string | null>(null);

  const save = () => {
    setError(null);
    onSave({
      email,
      name: name || null,
      language: language || null,
      externalId: externalId || null,
      fields: Object.fromEntries(fields.filter(([key, value]) => key.trim() !== "" && value.trim() !== "")),
      groups: groups.split(",").map((each) => each.trim()).filter(Boolean),
    }).then(
      () => onClose(true),
      (caught: unknown) => {
        const problem = caught instanceof ApiError ? caught.problem.errors : undefined;
        setError(problem?.email?.[0] === "email-taken" ? t("emailTaken") : problem?.email !== undefined ? t("emailInvalid") : problem?.externalId !== undefined ? t("externalIdTaken") : t("failed"));
      },
    );
  };

  return (
    <Dialog title={contact === null ? t("addContact").replace(/^\+ /, "") : (contact.name ?? contact.email)} kicker={t("contact")} onClose={() => onClose(false)}>
      <div className="person-panel__form">
        <label htmlFor={`${id}-email`}>{t("email")}</label>
        <input id={`${id}-email`} className="designer-input" type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
        <label htmlFor={`${id}-name`}>{t("name")}</label>
        <input id={`${id}-name`} className="designer-input" value={name} onChange={(event) => setName(event.target.value)} />
        <span id={`${id}-language`}>{t("language")}</span>
        <ComboBox
          labelledBy={`${id}-language`}
          options={[
            { value: "", label: t("languageNone") },
            { value: "de", label: "Deutsch" },
            { value: "en", label: "English" },
          ]}
          value={language}
          onChange={setLanguage}
          texts={{ choose: t("languageNone") }}
        />
        <label htmlFor={`${id}-external`}>{t("externalId")}</label>
        <span>
          <input id={`${id}-external`} className="designer-input person-panel__mono" value={externalId} aria-describedby={`${id}-external-hint`} onChange={(event) => setExternalId(event.target.value)} />
          <small id={`${id}-external-hint`} className="person-panel__hint">
            {t("externalIdHint")}
          </small>
        </span>
        <label htmlFor={`${id}-groups`}>{t("groups")}</label>
        <input id={`${id}-groups`} className="designer-input" value={groups} onChange={(event) => setGroups(event.target.value)} />
        {fields.map(([key, value], index) => (
          <ContactField key={index} id={`${id}-field-${index}`} name={key} value={value} prompt={t("fieldName")} onChange={(nextKey, nextValue) => setFields((current) => current.map((field, at) => (at === index ? [nextKey, nextValue] : field)))} />
        ))}
        <span />
        <span>
          <button type="button" className="designer-link-button" onClick={() => setFields((current) => [...current, ["", ""]])}>
            {t("addField")}
          </button>
        </span>
      </div>
      {error !== null && (
        <p className="designer-error" role="alert">
          {error}
        </p>
      )}
      <DialogActions>
        <button type="button" className="participants__button" onClick={() => onClose(false)}>
          {t("cancel")}
        </button>
        <button type="button" className="participants__button participants__button--primary" onClick={save}>
          {t("save")}
        </button>
      </DialogActions>
    </Dialog>
  );
}

function ContactField({ id, name, value, prompt, onChange }: { id: string; name: string; value: string; prompt: string; onChange: (name: string, value: string) => void }) {
  const [naming, setNaming] = useState(name === "");

  return (
    <>
      {naming ? (
        <input className="designer-input" aria-label={prompt} placeholder={prompt} value={name} onChange={(event) => onChange(event.target.value, value)} onBlur={() => name !== "" && setNaming(false)} />
      ) : (
        <label htmlFor={id}>{name}</label>
      )}
      <input id={id} className="designer-input" value={value} aria-label={naming ? name || prompt : undefined} onChange={(event) => onChange(name, event.target.value)} />
    </>
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

/** Invite the chosen contacts to a survey: published surveys only, newest first. */
function InviteToSurvey({ t, surveys, count, onInvite, onClose }: { t: (key: PeopleMessageKey, values?: Record<string, string | number>) => string; surveys: Pick<SurveysClient, "list">; count: number; onInvite: (survey: SurveyListItem) => Promise<string>; onClose: (notice?: string) => void }) {
  const id = useId();
  const [options, setOptions] = useState<SurveyListItem[] | null>(null);
  const [chosen, setChosen] = useState("");
  const [failed, setFailed] = useState<string | null>(null);

  useEffect(() => {
    surveys.list({ status: "published", limit: 100 }).then((answer) => {
      setOptions(answer.items);
      setChosen(answer.items[0]?.id ?? "");
    }, () => setOptions([]));
  }, [surveys]);

  const survey = options?.find((each) => each.id === chosen);

  return (
    <Dialog title={t("inviteToSurvey").replace("…", "")} onClose={() => onClose()}>
      <div className="person-panel__form">
        <span id={`${id}-survey`}>{t("chooseSurvey")}</span>
        <ComboBox labelledBy={`${id}-survey`} options={(options ?? []).map((each) => ({ value: each.id, label: each.title }))} value={chosen} onChange={setChosen} texts={{ choose: t("chooseSurvey") }} />
      </div>
      {failed !== null && <p className="designer-error">{failed}</p>}
      <DialogActions>
        <button type="button" className="participants__button" onClick={() => onClose()}>
          {t("cancel")}
        </button>
        <button
          type="button"
          className="participants__button participants__button--primary"
          disabled={survey === undefined}
          onClick={() => survey !== undefined && onInvite(survey).then(onClose, (caught: unknown) => setFailed(caught instanceof ApiError && caught.problem.code === "email-not-set-up" ? t("noEmail") : t("failed")))}
        >
          {t("inviteContacts", { count })}
        </button>
      </DialogActions>
    </Dialog>
  );
}
