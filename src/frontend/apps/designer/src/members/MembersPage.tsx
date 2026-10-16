// SPDX-License-Identifier: AGPL-3.0-only
import "../designer/designer.css";
import "./members.css";
import { ApiError, type Invited, type Member, type MemberInvitation, type MemberRole, type MembersClient, type MembersPage as Page } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, DataGrid, dataGridTexts, Dialog as SharedDialog, type DataGridColumn } from "@askwerk/ui";
import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { membersMessages, type MembersMessageKey } from "./messages";

type T = (key: MembersMessageKey, values?: Record<string, string | number>) => string;

const roles: readonly MemberRole[] = ["owner", "admin", "editor", "analyst", "viewer"];
const roleKey = (role: MemberRole) => role as MembersMessageKey;
const longKey = (role: MemberRole) => `${role}Long` as MembersMessageKey;
const hintKey = (role: MemberRole) => `${role}Hint` as MembersMessageKey;
const addressPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type MembersPageProps = {
  client: MembersClient;
  locale: Locale;
  /** The organisation's name, for the removal question. */
  organisation: string;
  writeClipboard?: (text: string) => Promise<void>;
  timeZone?: string;
  now?: () => Date;
};

/**
 * Organisation → Members (AWK-30, W18 A and B): the members with their role in the row, the open invitations under
 * them, inviting in one step, and removal after asking. Owners are managed by owners only; there is always an owner.
 */
export function MembersPage({ client, locale, organisation, writeClipboard = (text) => navigator.clipboard.writeText(text), timeZone, now = () => new Date() }: MembersPageProps) {
  const t: T = useMemo(() => createTranslator(membersMessages, locale), [locale]);
  const [page, setPage] = useState<Page | null>(null);
  const [state, setState] = useState<"loaded" | "failed" | "refused">("loaded");
  const [inviting, setInviting] = useState(false);
  const [removing, setRemoving] = useState<Member | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const day = new Intl.DateTimeFormat(locale === "de" ? "de-AT" : "en-US", { month: "short", day: "numeric", ...(timeZone === undefined ? {} : { timeZone }) });

  const reload = () =>
    client.list().then(
      (answer) => {
        setPage(answer);
        setState("loaded");
      },
      (failure: unknown) => setState(failure instanceof ApiError && failure.status === 403 ? "refused" : "failed"),
    );

  // Loads once per client; reload is recreated on every render.
  useEffect(() => {
    void reload();
  }, [client]);

  const act = (work: Promise<unknown>) =>
    work.then(
      () => {
        setError(null);
        return reload();
      },
      (failure: unknown) => setError(failure instanceof ApiError && (failure.problem as { code?: string }).code === "last-owner" ? t("lastOwner") : t("failed")),
    );

  const mayManage = (role: MemberRole) => page !== null && (page.canManageOwners || role !== "owner");
  const lastSignIn = (member: Member) => {
    if (member.lastSignInAt === null) {
      return t("never");
    }

    return day.format(new Date(member.lastSignInAt));
  };

  const columns: DataGridColumn<Member>[] = [
    { key: "name", header: t("name"), phone: "title", cell: (member) => (member.you ? <b>{t("you", { name: member.name })}</b> : member.name) },
    { key: "email", header: t("email"), cell: (member) => member.email },
    {
      key: "role",
      header: t("role"),
      cell: (member) =>
        member.managedBy != null ? (
          <span className="members__role">
            {t(roleKey(member.role))} <small className="members__muted">{member.managedBy === "" ? t("managedByProduct") : t("managedBy", { system: member.managedBy })}</small>
          </span>
        ) : member.you || !mayManage(member.role) ? (
          <span className="members__role">{t(roleKey(member.role))}</span>
        ) : (
          <RoleChoice t={t} member={member} canManageOwners={page?.canManageOwners ?? false} onChange={(role) => void act(client.changeRole(member.id, role))} />
        ),
    },
    { key: "twoFactor", header: t("twoFactor"), cell: (member) => (member.twoFactor ? t("on") : t("off")) },
    { key: "lastSignIn", header: t("lastSignIn"), cell: lastSignIn },
    {
      key: "remove",
      header: t("remove"),
      align: "end",
      cell: (member) =>
        member.you || member.managedBy != null || !mayManage(member.role) ? null : (
          <button type="button" className="members__link" aria-label={t("removeOf", { name: member.name })} onClick={() => setRemoving(member)}>
            {t("remove")}
          </button>
        ),
    },
  ];

  if (state === "refused") {
    return (
      <main className="members" lang={locale}>
        <h1>{t("title")}</h1>
        <p role="alert">{t("notAllowed")}</p>
      </main>
    );
  }

  const open = page?.invitations.length ?? 0;
  const count = page === null ? "" : page.members.length === 1 ? t("memberCountOne") : t("memberCount", { count: page.members.length });

  return (
    <main className="members" lang={locale}>
      <div className="members__head">
        <div>
          <h1>{t("title")}</h1>
          {page !== null && (
            <p className="members__lead">
              {open === 0 ? t("countsNoInvitation", { members: count }) : open === 1 ? t("countsOneInvitation", { members: count }) : t("counts", { members: count, invitations: open })}
            </p>
          )}
        </div>
        <button type="button" className="designer-button designer-button--primary members__invite" onClick={() => setInviting(true)}>
          {t("invite")}
        </button>
      </div>
      {notice !== null && (
        <p className="members__notice" role="status">
          {notice}
        </p>
      )}
      {error !== null && (
        <p className="members__error" role="alert">
          {error}
        </p>
      )}
      <DataGrid
        label={t("members")}
        columns={columns}
        rows={page?.members ?? null}
        rowKey={(member) => member.id}
        empty={null}
        error={state === "failed"}
        onRetry={() => void reload()}
        texts={dataGridTexts(locale === "de" ? "de" : "en")}
      />
      <details className="members__roles">
        <summary>{t("rolesExplained")}</summary>
        <dl>
          {roles.map((role) => (
            <div key={role}>
              <dt>{t(roleKey(role))}</dt>
              <dd>{t(hintKey(role))}</dd>
            </div>
          ))}
        </dl>
      </details>
      {page !== null && page.invitations.length > 0 && (
        <Invitations
          t={t}
          invitations={page.invitations}
          emailReady={page.emailReady}
          canManageOwners={page.canManageOwners}
          day={day}
          now={now}
          onCopy={(invitation) => client.link(invitation.id).then(writeClipboard)}
          onSendAgain={(invitation) => void act(client.sendAgain(invitation.id))}
          onRevoke={(invitation) => void act(client.revoke(invitation.id))}
        />
      )}
      {inviting && page !== null && (
        <InviteDialog
          t={t}
          client={client}
          members={page.members.map((member) => member.email.toLowerCase())}
          canManageOwners={page.canManageOwners}
          writeClipboard={writeClipboard}
          onClose={(invited) => {
            setInviting(false);

            if (invited !== undefined && invited.sent) {
              setNotice(invited.invitations.length === 1 ? t("invitedSentOne") : t("invitedSent", { count: invited.invitations.length }));
            }

            void reload();
          }}
        />
      )}
      {removing !== null && (
        <Dialog title={t("removeTitle")} onClose={() => setRemoving(null)}>
          <p>{t("removeAsk", { name: removing.name, organisation })}</p>
          <div className="designer-row">
            <button type="button" className="designer-button designer-button--quiet" autoFocus onClick={() => setRemoving(null)}>
              {t("cancel")}
            </button>
            <button
              type="button"
              className="designer-button designer-button--primary"
              onClick={() => {
                const member = removing;
                setRemoving(null);
                void act(client.remove(member.id));
              }}
            >
              {t("remove")}
            </button>
          </div>
        </Dialog>
      )}
    </main>
  );
}

function RoleChoice({ t, member, canManageOwners, onChange }: { t: T; member: Member; canManageOwners: boolean; onChange: (role: MemberRole) => void }) {
  const id = useId();
  const choices = roles.filter((role) => canManageOwners || role !== "owner");

  return (
    <span className="members__choice">
      <span id={id} className="members__hidden">
        {t("roleOf", { name: member.name })}
      </span>
      <ComboBox
        labelledBy={id}
        options={choices.map((role) => ({ value: role, label: t(roleKey(role)) }))}
        value={member.role}
        onChange={(value) => value !== member.role && onChange(value as MemberRole)}
        texts={{ choose: t("role") }}
      />
    </span>
  );
}

function Invitations({
  t,
  invitations,
  emailReady,
  canManageOwners,
  day,
  now,
  onCopy,
  onSendAgain,
  onRevoke,
}: {
  t: T;
  invitations: readonly MemberInvitation[];
  emailReady: boolean;
  canManageOwners: boolean;
  day: Intl.DateTimeFormat;
  now: () => Date;
  onCopy: (invitation: MemberInvitation) => Promise<unknown>;
  onSendAgain: (invitation: MemberInvitation) => void;
  onRevoke: (invitation: MemberInvitation) => void;
}) {
  const headingId = useId();
  const [copied, setCopied] = useState<string | null>(null);
  const line = (invitation: MemberInvitation) => {
    const values = { by: invitation.invitedBy, date: day.format(new Date(invitation.invitedAt)) };
    const days = Math.ceil((new Date(invitation.expiresAt).getTime() - now().getTime()) / 86_400_000);

    return !invitation.open || days <= 0 ? t("expired", values) : days === 1 ? t("invitedLineLast", values) : t("invitedLine", { ...values, days });
  };

  return (
    <section className="members__invitations" aria-labelledby={headingId}>
      <h2 id={headingId}>{t("invitations")}</h2>
      <ul>
        {invitations.map((invitation) => (
          <li key={invitation.id}>
            <span className="members__address">{invitation.email}</span>
            <span>{t(roleKey(invitation.role))}</span>
            <span className="members__muted">{line(invitation)}</span>
            <span className="members__pill">{invitation.sentAt === null ? t("linkOnly") : t("sent")}</span>
            {(canManageOwners || invitation.role !== "owner") && (
              <span className="members__actions">
                <button
                  type="button"
                  className="members__link"
                  aria-label={t("copyLinkFor", { email: invitation.email })}
                  onClick={() => void onCopy(invitation).then(() => setCopied(invitation.id), () => undefined)}
                >
                  {copied === invitation.id ? t("copied") : t("copyLink")}
                </button>
                {emailReady && (
                  <button type="button" className="members__link" aria-label={t("sendAgainFor", { email: invitation.email })} onClick={() => onSendAgain(invitation)}>
                    {t("sendAgain")}
                  </button>
                )}
                <button type="button" className="members__link" aria-label={t("revokeFor", { email: invitation.email })} onClick={() => onRevoke(invitation)}>
                  {t("revoke")}
                </button>
              </span>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Invite members (W18 B), one step: addresses checked as typed, one role, Editor first. */
function InviteDialog({
  t,
  client,
  members,
  canManageOwners,
  writeClipboard,
  onClose,
}: {
  t: T;
  client: MembersClient;
  members: readonly string[];
  canManageOwners: boolean;
  writeClipboard: (text: string) => Promise<void>;
  onClose: (invited?: Invited) => void;
}) {
  const id = useId();
  const [text, setText] = useState("");
  const [role, setRole] = useState<MemberRole>("editor");
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const [links, setLinks] = useState<Invited | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const field = useRef<HTMLTextAreaElement>(null);
  const lines = [...new Set(text.split(/[\n,;]+/).map((line) => line.trim().toLowerCase()).filter((line) => line !== ""))];
  const already = lines.filter((line) => members.includes(line));
  const invalid = lines.filter((line) => !addressPattern.test(line));
  const fresh = lines.filter((line) => !already.includes(line) && !invalid.includes(line));
  const choices = roles.filter((each) => canManageOwners || each !== "owner");

  useEffect(() => field.current?.focus(), []);

  const check = [
    ...(lines.length === 0 ? [] : [t("checkNew", { count: fresh.length })]),
    ...(already.length === 0 ? [] : [t("checkMembers", { list: already.join(", ") })]),
    ...(invalid.length === 0 ? [] : [t("checkInvalid", { list: invalid.join(", ") })]),
  ].join(" · ");

  return (
    <Dialog title={t("inviteTitle")} onClose={() => onClose(links ?? undefined)}>
      {links === null ? (
        <form
          className="designer-dialog__form"
          onSubmit={(event) => {
            event.preventDefault();
            setBusy(true);
            setFailed(false);
            client.invite(fresh, role).then(
              (invited) => (invited.sent ? onClose(invited) : setLinks(invited)),
              () => {
                setBusy(false);
                setFailed(true);
              },
            );
          }}
        >
          <label className="designer-label" htmlFor={`${id}-addresses`}>
            {t("addresses")}
          </label>
          <textarea ref={field} id={`${id}-addresses`} className="designer-input" rows={4} value={text} onChange={(event) => setText(event.target.value)} />
          {check !== "" && <p className="members__muted">{check}</p>}
          <span id={`${id}-role`} className="designer-label">
            {t("role")}
          </span>
          <div className="members__role-pick">
            <ComboBox labelledBy={`${id}-role`} options={choices.map((each) => ({ value: each, label: t(longKey(each)) }))} value={role} onChange={(value) => setRole(value as MemberRole)} texts={{ choose: t("role") }} />
            <span className="members__muted">{t(hintKey(role))}</span>
          </div>
          <p className="members__muted">{t("inviteValid")}</p>
          {failed && <p className="designer-error">{t("failed")}</p>}
          <div className="designer-row">
            <button type="button" className="designer-button designer-button--quiet" onClick={() => onClose()}>
              {t("cancel")}
            </button>
            <button type="submit" className="designer-button designer-button--primary" disabled={busy || fresh.length === 0}>
              {t("inviteCount", { count: fresh.length })}
            </button>
          </div>
        </form>
      ) : (
        <>
          <p>{t("emailNotReady")}</p>
          <ul className="members__links">
            {links.invitations.map((invitation) => (
              <li key={invitation.id}>
                <span className="members__address">{invitation.email}</span>
                <button
                  type="button"
                  className="members__link"
                  aria-label={t("copyLinkFor", { email: invitation.email })}
                  onClick={() => void writeClipboard(invitation.link).then(() => setCopied(invitation.id), () => undefined)}
                >
                  {copied === invitation.id ? t("copied") : t("copyLink")}
                </button>
              </li>
            ))}
          </ul>
          <div className="designer-row">
            <button type="button" className="designer-button designer-button--primary" onClick={() => onClose(links)}>
              {t("done")}
            </button>
          </div>
        </>
      )}
    </Dialog>
  );
}

function Dialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  return (
    <SharedDialog title={title} onClose={onClose} className="members__dialog">
      {children}
    </SharedDialog>
  );
}
