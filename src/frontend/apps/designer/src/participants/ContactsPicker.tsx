// SPDX-License-Identifier: AGPL-3.0-only
import type { ContactItem, ContactList, ContactsClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, Dialog, DialogActions } from "@askwerk/ui";
import { useEffect, useId, useMemo, useState } from "react";
import { peopleMessages } from "./peopleMessages";

/**
 * Invite from contacts (W35): a group or a search, then the people to invite. Who unsubscribed or whose address was
 * refused cannot be chosen.
 */
export function ContactsPicker({ contacts, locale, onInvite, onClose }: { contacts: ContactsClient; locale: Locale; onInvite: (contactIds: string[]) => Promise<string>; onClose: (notice?: string) => void }) {
  const t = useMemo(() => createTranslator(peopleMessages, locale), [locale]);
  const id = useId();
  const [group, setGroup] = useState("");
  const [search, setSearch] = useState("");
  const [list, setList] = useState<ContactList | null>(null);
  const [chosen, setChosen] = useState<ReadonlySet<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => void contacts.list({ ...(group === "" ? {} : { group }), search, state: "invitable", limit: 100 }).then(setList, () => setFailed(true)), 150);
    return () => clearTimeout(timer);
  }, [contacts, group, search]);

  const toggle = (contact: ContactItem) => setChosen((current) => (current.has(contact.id) ? new Set([...current].filter((each) => each !== contact.id)) : new Set([...current, contact.id])));
  const allShown = list !== null && list.items.length > 0 && list.items.every((contact) => chosen.has(contact.id));

  return (
    <Dialog title={t("pickContacts")} onClose={() => onClose()} wide>
      <div className="contacts-picker">
        <div className="contacts-picker__tools">
          <span id={`${id}-group`}>{t("pickGroup")}</span>
          <ComboBox
            labelledBy={`${id}-group`}
            options={[{ value: "", label: t("allContacts") }, ...(list?.groups ?? []).map((each) => ({ value: each.name, label: `${each.name} · ${each.count}` }))]}
            value={group}
            onChange={setGroup}
            texts={{ choose: t("allContacts") }}
          />
          <input type="search" className="participants__search" aria-label={t("searchContacts")} placeholder={t("searchContacts")} value={search} onChange={(event) => setSearch(event.target.value)} />
        </div>
        <label className="contacts-picker__all">
          <input type="checkbox" checked={allShown} onChange={() => setChosen((current) => (allShown ? new Set() : new Set([...current, ...(list?.items ?? []).map((contact) => contact.id)])))} />
          {t("chosenContacts", { count: chosen.size })}
        </label>
        <ul className="contacts-picker__list" aria-label={t("contacts")}>
          {(list?.items ?? []).map((contact) => (
            <li key={contact.id}>
              <label>
                <input type="checkbox" checked={chosen.has(contact.id)} onChange={() => toggle(contact)} />
                <span className="participants__who">
                  <b>{contact.name ?? contact.email}</b>
                  <span>{[contact.email, contact.externalId, ...contact.groups].filter(Boolean).join(" · ")}</span>
                </span>
              </label>
            </li>
          ))}
        </ul>
        {failed && <p className="designer-error">{t("failed")}</p>}
        <DialogActions>
          <button type="button" className="designer-button designer-button--quiet" onClick={() => onClose()}>
            {t("cancel")}
          </button>
          <button
            type="button"
            className="designer-button designer-button--primary"
            disabled={busy || chosen.size === 0}
            onClick={() => {
              setBusy(true);
              onInvite([...chosen]).then(onClose, () => {
                setBusy(false);
                setFailed(true);
              });
            }}
          >
            {t("inviteContacts", { count: chosen.size })}
          </button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
