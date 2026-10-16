// SPDX-License-Identifier: AGPL-3.0-only
import "./admin.css";
import { ApiError, type CreatedOrganisation, type InstallationAdministrator, type InstallationClient, type InstallationInfo, type InstallationOrganisations, type MembersClient, type OrganisationClient, type OrganisationDetails, type RemovalCounts } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, ConfirmDialog, DataGrid, dataGridTexts, Dialog, DialogActions, Field, type DataGridColumn } from "@askwerk/ui";
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { adminMessages, type AdminMessageKey } from "./messages";

type T = (key: AdminMessageKey, values?: Record<string, string | number>) => string;
const useT = (locale: Locale): T => useMemo(() => createTranslator(adminMessages, locale), [locale]);
const day = (locale: Locale, at: string) => new Intl.DateTimeFormat(locale === "de" ? "de" : "en-GB", { day: "numeric", month: "short", year: "numeric" }).format(new Date(at));
const fieldCode = (error: unknown, field: string) => (error instanceof ApiError ? error.problem.errors?.[field]?.[0] : undefined);

/**
 * Administration → Organisation (AWK-38, W29): the organisation's name and short name, its time zone, and for owners
 * transferring ownership and deleting it.
 */
export function OrganisationPage({ client, members, locale, onDeleted }: { client: OrganisationClient; members: Pick<MembersClient, "list">; locale: Locale; onDeleted: () => void }) {
  const t = useT(locale);
  const id = useId();
  const [details, setDetails] = useState<OrganisationDetails | null>(null);
  const [failed, setFailed] = useState(false);
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [errors, setErrors] = useState<{ name?: string; slug?: string }>({});
  const [saved, setSaved] = useState(false);
  const [dialog, setDialog] = useState<"transfer" | "delete" | null>(null);
  const [people, setPeople] = useState<{ id: string; name: string }[]>([]);
  const [newOwner, setNewOwner] = useState("");

  const load = useCallback(() => {
    client.get().then(
      (loaded) => {
        setDetails(loaded);
        setName(loaded.name);
        setSlug(loaded.slug);
      },
      () => setFailed(true),
    );
  }, [client]);

  useEffect(load, [load]);

  if (details === null) {
    return <main className="admin" aria-busy={!failed}>{failed && <p role="alert">{t("failed")}</p>}</main>;
  }

  const manager = details.role === "owner" || details.role === "admin";
  const save = () => {
    setSaved(false);
    setErrors({});
    client.change(name.trim(), slug.trim()).then(
      () => (setSaved(true), load()),
      (error: unknown) => {
        const nameCode = fieldCode(error, "name");
        const slugCode = fieldCode(error, "slug");
        setErrors({
          ...(nameCode !== undefined && { name: t("nameRequired") }),
          ...(slugCode !== undefined && { slug: slugCode === "slug-taken" ? t("slugTaken") : t("slugInvalid") }),
          ...(nameCode === undefined && slugCode === undefined && { name: t("saveFailed") }),
        });
      },
    );
  };

  return (
    <main className="admin" lang={locale}>
      <p className="admin__kicker">{t("administration")}</p>
      <h1>{t("organisation")}</h1>
      <p className="admin__lead">{t("organisationLead", { name: details.name, date: day(locale, details.createdAt), members: details.members, surveys: details.surveys })}</p>

      <section className="admin__section" aria-labelledby={`${id}-name`}>
        <div>
          <h2 id={`${id}-name`}>{t("nameAndMark")}</h2>
          <p>{t("nameAndMarkText")}</p>
        </div>
        <div className="admin__form">
          <Field label={t("name")} {...(errors.name !== undefined && { error: errors.name })}>
            <input className="admin__input" maxLength={200} value={name} readOnly={!manager} onChange={(event) => (setName(event.target.value), setSaved(false))} />
          </Field>
        </div>
      </section>
      <section className="admin__section" aria-labelledby={`${id}-address`}>
        <div>
          <h2 id={`${id}-address`}>{t("address")}</h2>
          <p>{t("addressText")}</p>
        </div>
        <div className="admin__form">
          <Field label={t("shortName")} {...(errors.slug !== undefined && { error: errors.slug })}>
            <input className="admin__input admin__mono" maxLength={40} value={slug} readOnly={!manager} onChange={(event) => (setSlug(event.target.value), setSaved(false))} />
          </Field>
          {manager && (
            <div className="admin__row">
              <button type="button" className="admin__button admin__button--primary" disabled={name.trim() === details.name && slug.trim() === details.slug} onClick={save}>
                {t("save")}
              </button>
              {saved && <span role="status">{t("saved")}</span>}
            </div>
          )}
        </div>
      </section>
      <section className="admin__section" aria-labelledby={`${id}-zone`}>
        <div>
          <h2 id={`${id}-zone`}>{t("defaults")}</h2>
          <p>{t("defaultsText")}</p>
        </div>
        <div className="admin__form">
          <p className="admin__value admin__mono">{details.timeZone}</p>
        </div>
      </section>
      {details.role === "owner" && (
        <section className="admin__section" aria-labelledby={`${id}-owner`}>
          <div>
            <h2 id={`${id}-owner`}>{t("ownership")}</h2>
            <p>{t("ownershipText")}</p>
          </div>
          <div className="admin__form">
            <div className="admin__danger">
              <div>
                <b>{t("transfer")}</b>
                <p>{t("transferText")}</p>
              </div>
              <button
                type="button"
                className="admin__button"
                onClick={() => {
                  setDialog("transfer");
                  members.list().then((list) => setPeople(list.members.filter((member) => !member.you).map((member) => ({ id: member.id, name: member.name }))), () => setPeople([]));
                }}
              >
                {t("transferButton")}
              </button>
            </div>
            <div className="admin__danger admin__danger--red">
              <div>
                <b>{t("deleteOrganisation")}</b>
                <p>{t("deleteText")}</p>
              </div>
              <button type="button" className="admin__button admin__button--red" onClick={() => setDialog("delete")}>
                {t("deleteButton")}
              </button>
            </div>
          </div>
        </section>
      )}
      {dialog === "transfer" && (
        <Dialog title={t("transfer")} onClose={() => setDialog(null)}>
          <span className="admin__label" id={`${id}-to`}>
            {t("transferTo")}
          </span>
          <ComboBox id={`${id}-owner`} labelledBy={`${id}-to`} options={people.map((person) => ({ value: person.id, label: person.name }))} value={newOwner} onChange={setNewOwner} texts={{ choose: t("transferTo") }} />
          <DialogActions>
            <button type="button" className="admin__button" onClick={() => setDialog(null)}>
              {t("cancel")}
            </button>
            <button type="button" className="admin__button admin__button--primary" disabled={newOwner === ""} onClick={() => void client.transfer(newOwner).then(() => (setDialog(null), load()))}>
              {t("transferConfirm")}
            </button>
          </DialogActions>
        </Dialog>
      )}
      {dialog === "delete" && (
        <ConfirmDialog title={t("deleteOrganisation")} text={t("deleteAsk", { name: details.name })} confirm={t("deleteConfirm")} cancel={t("cancel")} onCancel={() => setDialog(null)} onConfirm={() => void client.remove().then(onDeleted)} />
      )}
    </main>
  );
}

/** Administration → This Askwerk → Organisations (AWK-38, W29): every organisation, create, suspend, resume, delete. */
/** Whole days until a deleted organisation's data is removed, at least 0. */
const daysUntil = (at: string) => Math.max(0, Math.ceil((new Date(at).getTime() - Date.now()) / 86_400_000));

export function OrganisationsPage({ client, locale, startNew = false }: { client: Pick<InstallationClient, "organisations" | "createOrganisation" | "suspend" | "resume" | "deleteOrganisation" | "restore" | "removal" | "removeData">; locale: Locale; startNew?: boolean }) {
  const t = useT(locale);
  const id = useId();
  const [list, setList] = useState<InstallationOrganisations | null>(null);
  const [failed, setFailed] = useState(false);
  const [creating, setCreating] = useState(startNew);
  const [removing, setRemoving] = useState<{ id: string; name: string } | null>(null);
  const [created, setCreated] = useState<{ name: string; answer: CreatedOrganisation } | null>(null);
  const [purging, setPurging] = useState<{ id: string; name: string; slug: string } | null>(null);
  const [purged, setPurged] = useState<string | null>(null);
  const load = useCallback(() => {
    client.organisations().then(setList, () => setFailed(true));
  }, [client]);

  useEffect(load, [load]);

  type Row = InstallationOrganisations["items"][number];
  const columns: DataGridColumn<Row>[] = [
    {
      key: "name",
      header: t("colOrganisation"),
      phone: "title",
      cell: (row) => (
        <span className="admin__org">
          <span className="admin__mark" aria-hidden="true">
            {row.name.slice(0, 2).toUpperCase()}
          </span>
          <b>{row.name}</b>
        </span>
      ),
    },
    { key: "slug", header: t("colShortName"), cell: (row) => <span className="admin__mono">{row.slug}</span> },
    { key: "owner", header: t("colOwner"), cell: (row) => row.owner ?? <span className="admin__muted">{t("ownerPending")}</span> },
    { key: "members", header: t("colMembers"), align: "end", cell: (row) => row.members },
    { key: "surveys", header: t("colSurveys"), align: "end", cell: (row) => row.surveys },
    { key: "responses", header: t("colResponses"), align: "end", cell: (row) => row.responses.toLocaleString(locale === "de" ? "de" : "en-GB") },
    { key: "created", header: t("colCreated"), cell: (row) => new Intl.DateTimeFormat(locale === "de" ? "de" : "en-GB", { day: "numeric", month: "short" }).format(new Date(row.createdAt)) },
    {
      key: "status",
      header: t("colStatus"),
      cell: (row) => (
        <>
          <span className={`admin__status admin__status--${row.status}`}>{t(row.status)}</span>
          {row.status === "deleted" && row.removesAt != null && <span className="admin__countdown">{t("daysLeft", { days: daysUntil(row.removesAt) })}</span>}
        </>
      ),
    },
    {
      key: "actions",
      header: t("colActions"),
      align: "end",
      // Buttons, not a menu: grid cells clip what overflows them.
      cell: (row) => {
        const action = (label: string, run: () => void, red = false) => (
          <button type="button" className={red ? "admin__linkbutton admin__linkbutton--red" : "admin__linkbutton"} aria-label={`${label.replace("…", "")}: ${row.name}`} onClick={run}>
            {label}
          </button>
        );

        return (
          <span className="admin__row admin__actions">
            {row.status === "deleted"
              ? [action(t("restore"), () => void client.restore(row.id).then(load)), action(t("removeNow"), () => setPurging({ id: row.id, name: row.name, slug: row.slug }), true)]
              : [
                  row.status === "active" ? action(t("suspend"), () => void client.suspend(row.id).then(load)) : action(t("resume"), () => void client.resume(row.id).then(load)),
                  action(t("remove"), () => setRemoving({ id: row.id, name: row.name }), true),
                ]}
          </span>
        );
      },
    },
  ];

  return (
    <main className="admin admin--wide" lang={locale}>
      <p className="admin__kicker">{t("thisAskwerk")}</p>
      <div className="admin__head">
        <div>
          <h1>{t("organisations")}</h1>
          <p className="admin__lead">{t("organisationsLead")}</p>
        </div>
        <button type="button" className="admin__button admin__button--primary" onClick={() => setCreating(true)}>
          {t("newOrganisation")}
        </button>
      </div>
      {created !== null && (
        <p className="admin__done" role="status">
          {created.answer.ownerJoined
            ? t("createdJoined", { name: created.name })
            : created.answer.invitationSent
              ? t("createdSent", { name: created.name })
              : t("createdInvited", { name: created.name })}
          {!created.answer.ownerJoined && !created.answer.invitationSent && created.answer.invitationLink !== null && <code className="admin__link">{created.answer.invitationLink}</code>}
        </p>
      )}
      {purged !== null && (
        <p className="admin__done" role="status">
          {t("removedData", { name: purged })}
        </p>
      )}
      {list !== null && (
        <dl className="admin__totals">
          <div>
            <dt>{t("totalOrganisations")}</dt>
            <dd>{list.items.filter((item) => item.status !== "deleted").length}</dd>
          </div>
          <div>
            <dt>{t("totalMembers")}</dt>
            <dd>{list.members}</dd>
          </div>
          <div>
            <dt>{t("totalSurveys")}</dt>
            <dd>{list.surveys}</dd>
          </div>
          <div>
            <dt>{t("totalResponses")}</dt>
            <dd>{list.recentResponses.toLocaleString(locale === "de" ? "de" : "en-GB")}</dd>
          </div>
        </dl>
      )}
      <DataGrid label={t("organisations")} columns={columns} rows={list?.items ?? null} rowKey={(row) => row.id} empty={null} error={failed} onRetry={load} texts={dataGridTexts(locale === "de" ? "de" : "en")} />
      {creating && (
        <NewOrganisationDialog
          t={t}
          id={id}
          onCancel={() => setCreating(false)}
          onCreate={(name, slug, owner) =>
            client.createOrganisation(name, slug, owner).then((answer) => {
              setCreated({ name, answer });
              setCreating(false);
              load();
            })
          }
        />
      )}
      {removing !== null && (
        <ConfirmDialog
          title={t("remove").replace("…", "")}
          text={t("removeAsk", { name: removing.name })}
          confirm={t("remove").replace("…", "")}
          cancel={t("cancel")}
          onCancel={() => setRemoving(null)}
          onConfirm={() => void client.deleteOrganisation(removing.id).then(() => (setRemoving(null), load()))}
        />
      )}
      {purging !== null && (
        <RemoveDataDialog
          t={t}
          organisation={purging}
          counts={() => client.removal(purging.id)}
          onCancel={() => setPurging(null)}
          onRemove={() =>
            client.removeData(purging.id, purging.slug).then(() => {
              setPurged(purging.name);
              setPurging(null);
              load();
            })
          }
        />
      )}
    </main>
  );
}

/** Removing a deleted organisation's data now (AWK-39, W30): what goes, confirmed by typing the short name. */
function RemoveDataDialog({ t, organisation, counts, onCancel, onRemove }: { t: T; organisation: { name: string; slug: string }; counts: () => Promise<RemovalCounts>; onCancel: () => void; onRemove: () => Promise<void> }) {
  const id = useId();
  const [loaded, setLoaded] = useState<RemovalCounts | null>(null);
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    counts().then(setLoaded, () => setFailed(true));
    // Counted once when the dialog opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const rows: [AdminMessageKey, number][] = loaded === null ? [] : [["countSurveys", Number(loaded.surveys)], ["countResponses", Number(loaded.responses)], ["countMembers", Number(loaded.members)], ["countOther", Number(loaded.other)]];

  return (
    <Dialog title={t("removeTitle", { name: organisation.name })} onClose={onCancel}>
      <p>{t("removeText")}</p>
      <dl className="admin__counts" aria-busy={loaded === null && !failed}>
        {rows.map(([key, value]) => (
          <div key={key}>
            <dt>{t(key)}</dt>
            <dd>{value.toLocaleString()}</dd>
          </div>
        ))}
      </dl>
      <label className="admin__label" htmlFor={`${id}-slug`}>
        {t("typeToConfirm", { slug: organisation.slug })}
      </label>
      <input id={`${id}-slug`} className="admin__input admin__mono" autoComplete="off" spellCheck={false} value={typed} onChange={(event) => setTyped(event.target.value)} />
      {failed && (
        <p className="admin__problem" role="alert">
          {t("failed")}
        </p>
      )}
      <DialogActions>
        <button type="button" className="admin__button" onClick={onCancel}>
          {t("cancel")}
        </button>
        <button
          type="button"
          className="admin__button admin__button--red"
          disabled={typed.trim() !== organisation.slug || busy}
          onClick={() => {
            setBusy(true);
            setFailed(false);
            onRemove().catch(() => (setBusy(false), setFailed(true)));
          }}
        >
          {t("removeData")}
        </button>
      </DialogActions>
    </Dialog>
  );
}

function NewOrganisationDialog({ t, id, onCancel, onCreate }: { t: T; id: string; onCancel: () => void; onCreate: (name: string, slug: string, owner: string) => Promise<void> }) {
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [owner, setOwner] = useState("");
  const [errors, setErrors] = useState<{ name?: string; slug?: string; owner?: string }>({});

  return (
    <Dialog title={t("newOrganisation")} onClose={onCancel}>
      <form
        className="admin__form"
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          setErrors({});
          onCreate(name.trim(), slug.trim(), owner.trim()).catch((error: unknown) => {
            const slugCode = fieldCode(error, "slug");
            setErrors({
              ...(fieldCode(error, "name") !== undefined && { name: t("nameRequired") }),
              ...(slugCode !== undefined && { slug: slugCode === "slug-taken" ? t("slugTaken") : t("slugInvalid") }),
              ...(fieldCode(error, "ownerEmail") !== undefined && { owner: t("emailInvalid") }),
            });
          });
        }}
      >
        <Field label={t("name")} {...(errors.name !== undefined && { error: errors.name })}>
          <input id={`${id}-new-name`} className="admin__input" maxLength={200} value={name} onChange={(event) => setName(event.target.value)} />
        </Field>
        <Field label={t("shortName")} {...(errors.slug !== undefined && { error: errors.slug })}>
          <input className="admin__input admin__mono" maxLength={40} value={slug} placeholder={name.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "")} onChange={(event) => setSlug(event.target.value)} />
        </Field>
        <Field label={t("ownerEmail")} hint={t("ownerEmailNote")} {...(errors.owner !== undefined && { error: errors.owner })}>
          <input className="admin__input" type="email" value={owner} onChange={(event) => setOwner(event.target.value)} />
        </Field>
        <DialogActions>
          <button type="button" className="admin__button" onClick={onCancel}>
            {t("cancel")}
          </button>
          <button type="submit" className="admin__button admin__button--primary">
            {t("create")}
          </button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** Administration → This Askwerk → Administrators (AWK-38, W29). */
export function AdministratorsPage({ client, locale }: { client: Pick<InstallationClient, "administrators" | "addAdministrator" | "removeAdministrator">; locale: Locale }) {
  const t = useT(locale);
  const [items, setItems] = useState<InstallationAdministrator[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [email, setEmail] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const load = useCallback(() => {
    client.administrators().then(setItems, () => setFailed(true));
  }, [client]);

  useEffect(load, [load]);

  const columns: DataGridColumn<InstallationAdministrator>[] = [
    { key: "name", header: t("name"), phone: "title", cell: (row) => <b>{row.name}{row.me && <span className="admin__muted"> · {t("you")}</span>}</b> },
    { key: "email", header: "Email", cell: (row) => row.email },
    {
      key: "remove",
      header: t("colActions"),
      align: "end",
      cell: (row) => (
        <button
          type="button"
          className="admin__linkbutton"
          onClick={() => {
            setProblem(null);
            client.removeAdministrator(row.id).then(load, (error: unknown) => setProblem(error instanceof ApiError && error.status === 409 ? t("lastAdmin") : t("saveFailed")));
          }}
        >
          {t("removeAdministrator")}
        </button>
      ),
    },
  ];

  return (
    <main className="admin" lang={locale}>
      <p className="admin__kicker">{t("thisAskwerk")}</p>
      <h1>{t("administrators")}</h1>
      <p className="admin__lead">{t("administratorsLead")}</p>
      <form
        className="admin__row admin__add"
        onSubmit={(event) => {
          event.preventDefault();
          setProblem(null);
          client.addAdministrator(email.trim()).then(
            () => (setEmail(""), load()),
            (error: unknown) => setProblem(error instanceof ApiError && error.status === 404 ? t("userNotFound") : t("saveFailed")),
          );
        }}
      >
        <Field label={t("administratorEmail")}>
          <input className="admin__input" type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
        </Field>
        <button type="submit" className="admin__button" disabled={email.trim() === ""}>
          {t("addAdministrator")}
        </button>
      </form>
      {problem !== null && (
        <p className="admin__problem" role="alert">
          {problem}
        </p>
      )}
      <DataGrid label={t("administrators")} columns={columns} rows={items} rowKey={(row) => row.id} empty={null} error={failed} onRetry={load} texts={dataGridTexts(locale === "de" ? "de" : "en")} />
    </main>
  );
}

/** Every time zone the browser knows, with the installation's own first if it is missing. */
function timeZones(current: string): string[] {
  const known = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];

  return known.includes(current) || current === "" ? known : [current, ...known];
}

type InstallationForm = { name: string; defaultLanguage: string; timeZone: string; smtpHost: string; smtpPort: string; smtpUserName: string; smtpPassword: string; smtpSender: string };

const formOf = (info: InstallationInfo): InstallationForm => ({
  name: info.name,
  defaultLanguage: info.defaultLanguage,
  timeZone: info.timeZone,
  smtpHost: info.smtpHost ?? "",
  smtpPort: String(info.smtpPort ?? 587),
  smtpUserName: info.smtpUserName ?? "",
  smtpPassword: "",
  smtpSender: info.smtpSender ?? "",
});

/**
 * Administration → This Askwerk → Installation (AWK-38, W29; editable since AWK-39, W30): the name, the defaults for
 * new organisations, email with a test, and the address. What the configuration fixes is shown, locked.
 */
export function InstallationPage({ client, locale }: { client: Pick<InstallationClient, "info" | "change" | "testEmail">; locale: Locale }) {
  const t = useT(locale);
  const id = useId();
  const [info, setInfo] = useState<InstallationInfo | null>(null);
  const [form, setForm] = useState<InstallationForm | null>(null);
  const [failed, setFailed] = useState(false);
  const [errors, setErrors] = useState<Partial<Record<keyof InstallationForm, string>>>({});
  const [saved, setSaved] = useState(false);
  const [test, setTest] = useState<{ ok: true; to: string; at: Date } | { ok: false } | null>(null);
  const zones = useMemo(() => timeZones(info?.timeZone ?? "").map((zone) => ({ value: zone, label: zone })), [info?.timeZone]);

  const load = useCallback(() => {
    client.info().then((loaded) => (setInfo(loaded), setForm(formOf(loaded))), () => setFailed(true));
  }, [client]);

  useEffect(load, [load]);

  if (info === null || form === null) {
    return (
      <main className="admin" lang={locale} aria-busy={!failed}>
        {failed && <p role="alert">{t("failed")}</p>}
      </main>
    );
  }

  const locked = new Set(info.locked ?? ["baseAddress"]);
  const emailLocked = locked.has("email");
  const set = (key: keyof InstallationForm) => (value: string) => (setForm({ ...form, [key]: value }), setSaved(false));
  const lock = (key: string) => locked.has(key) && <span className="admin__lock">{t("lockedByConfiguration")}</span>;
  const port = Number.parseInt(form.smtpPort, 10);
  const email = { host: form.smtpHost.trim(), port: Number.isNaN(port) ? null : port, userName: form.smtpUserName.trim() || null, password: form.smtpPassword === "" ? null : form.smtpPassword, sender: form.smtpSender.trim() };

  const save = () => {
    setSaved(false);
    setErrors({});
    client
      .change({ name: form.name.trim(), defaultLanguage: form.defaultLanguage, timeZone: form.timeZone, smtpHost: email.host, smtpPort: email.port, smtpUserName: email.userName, smtpPassword: email.password, smtpSender: email.sender })
      .then(
        () => (setSaved(true), load()),
        (error: unknown) => {
          const found: Partial<Record<keyof InstallationForm, string>> = {
            ...(fieldCode(error, "name") !== undefined && { name: t("nameRequired") }),
            ...(fieldCode(error, "timeZone") !== undefined && { timeZone: t("timeZoneInvalid") }),
            ...(fieldCode(error, "smtpPort") !== undefined && { smtpPort: t("portInvalid") }),
            ...(fieldCode(error, "smtpSender") !== undefined && { smtpSender: t("senderInvalid") }),
          };
          setErrors(Object.keys(found).length > 0 ? found : { name: t("saveFailed") });
        },
      );
  };

  return (
    <main className="admin" lang={locale}>
      <p className="admin__kicker">{t("thisAskwerk")}</p>
      <h1>{t("installation")}</h1>
      <p className="admin__lead">{t("installationVersionLead", { version: info.version })}</p>

      <section className="admin__section" aria-labelledby={`${id}-name`}>
        <div>
          <h2 id={`${id}-name`}>{t("installationName")}</h2>
          <p>{t("installationNameText")}</p>
        </div>
        <div className="admin__form">
          <Field label={t("name")} {...(errors.name !== undefined && { error: errors.name })}>
            <input className="admin__input" maxLength={200} value={form.name} readOnly={locked.has("name")} onChange={(event) => set("name")(event.target.value)} />
          </Field>
          {lock("name")}
        </div>
      </section>
      <section className="admin__section" aria-labelledby={`${id}-defaults`}>
        <div>
          <h2 id={`${id}-defaults`}>{t("installationDefaults")}</h2>
          <p>{t("installationDefaultsText")}</p>
        </div>
        <div className="admin__form admin__pair">
          <div>
            <span className="aw-label" id={`${id}-language`}>
              {t("installationLanguage")}
              {lock("defaultLanguage")}
            </span>
            {locked.has("defaultLanguage") ? (
              <input className="admin__input" readOnly aria-labelledby={`${id}-language`} value={form.defaultLanguage === "de" ? t("languageGerman") : t("languageEnglish")} />
            ) : (
              <ComboBox labelledBy={`${id}-language`} options={[{ value: "de", label: t("languageGerman") }, { value: "en", label: t("languageEnglish") }]} value={form.defaultLanguage} onChange={set("defaultLanguage")} texts={{ choose: t("installationLanguage") }} />
            )}
          </div>
          <div>
            <span className="aw-label" id={`${id}-zone`}>
              {t("installationTimeZone")}
              {lock("timeZone")}
            </span>
            {locked.has("timeZone") ? (
              <input className="admin__input admin__mono" readOnly aria-labelledby={`${id}-zone`} value={form.timeZone} />
            ) : (
              <ComboBox
                labelledBy={`${id}-zone`}
                options={zones}
                value={form.timeZone}
                onChange={set("timeZone")}
                invalid={errors.timeZone !== undefined}
                texts={{ choose: t("chooseTimeZone"), search: t("searchTimeZone"), results: (shown, total) => `${shown} / ${total}`, noResults: "—" }}
              />
            )}
            {errors.timeZone !== undefined && (
              <p className="admin__problem" role="alert">
                {errors.timeZone}
              </p>
            )}
          </div>
        </div>
      </section>
      <section className="admin__section" aria-labelledby={`${id}-email`}>
        <div>
          <h2 id={`${id}-email`}>{t("installationEmail")}</h2>
          <p>{t("emailText")}</p>
        </div>
        <div className="admin__form">
          {lock("email")}
          <div className="admin__pair">
            <Field label={t("smtpServer")}>
              <input className="admin__input admin__mono" value={form.smtpHost} readOnly={emailLocked} onChange={(event) => set("smtpHost")(event.target.value)} />
            </Field>
            <Field label={t("smtpPort")} {...(errors.smtpPort !== undefined && { error: errors.smtpPort })}>
              <input className="admin__input admin__mono" inputMode="numeric" value={form.smtpPort} readOnly={emailLocked} onChange={(event) => set("smtpPort")(event.target.value)} />
            </Field>
          </div>
          <div className="admin__pair">
            <Field label={t("smtpUser")}>
              <input className="admin__input admin__mono" autoComplete="off" value={form.smtpUserName} readOnly={emailLocked} onChange={(event) => set("smtpUserName")(event.target.value)} />
            </Field>
            <Field label={t("smtpPassword")}>
              <input className="admin__input admin__mono" type="password" autoComplete="new-password" placeholder={info.smtpPasswordSet ? t("smtpPasswordKept") : ""} value={form.smtpPassword} readOnly={emailLocked} onChange={(event) => set("smtpPassword")(event.target.value)} />
            </Field>
          </div>
          <Field label={t("smtpSender")} {...(errors.smtpSender !== undefined && { error: errors.smtpSender })}>
            <input className="admin__input" value={form.smtpSender} readOnly={emailLocked} onChange={(event) => set("smtpSender")(event.target.value)} />
          </Field>
          <div className="admin__row">
            <button
              type="button"
              className="admin__button"
              disabled={!emailLocked && (email.host === "" || email.sender === "")}
              onClick={() => {
                setTest(null);
                client.testEmail(email).then((to) => setTest({ ok: true, to, at: new Date() }), () => setTest({ ok: false }));
              }}
            >
              {t("sendTest")}
            </button>
            {test?.ok === true && (
              <span className="admin__ok" role="status">
                ✓ {t("testSent", { to: test.to, time: new Intl.DateTimeFormat(locale === "de" ? "de" : "en-GB", { hour: "2-digit", minute: "2-digit" }).format(test.at) })}
              </span>
            )}
            {test?.ok === false && (
              <span className="admin__problem" role="alert">
                {t("testFailed")}
              </span>
            )}
            <span className="admin__grow" />
            {saved && <span role="status">{t("saved")}</span>}
            <button type="button" className="admin__button admin__button--primary" onClick={save}>
              {t("save")}
            </button>
          </div>
        </div>
      </section>
      <section className="admin__section" aria-labelledby={`${id}-address`}>
        <div>
          <h2 id={`${id}-address`}>{t("installationAddress")}</h2>
          <p>{t("installationAddressText")}</p>
        </div>
        <div className="admin__form">
          <Field label={t("installationAddress")}>
            <input className="admin__input admin__mono" readOnly value={info.baseAddress} />
          </Field>
          {lock("baseAddress")}
        </div>
      </section>
    </main>
  );
}
