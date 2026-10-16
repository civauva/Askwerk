// SPDX-License-Identifier: AGPL-3.0-only
import type { AuthClient, Me } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { Menu, MenuItem } from "@askwerk/ui";
import { accountMessages } from "./messages";

/** The initials on the avatar: the first letters of the first two words of the name. */
export const initialsOf = (name: string) =>
  name
    .split(/\s+/)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? "")
    .join("");

/**
 * The profile at the far right of a survey's bar (W23), as in the app bar of every other page: who is signed in,
 * Account and security, Sign out.
 */
export function ProfileMenu({ client, locale, me, onOpenAccount, onSignedOut }: { client: Pick<AuthClient, "signOut">; locale: Locale; me: Pick<Me, "name" | "email">; onOpenAccount: () => void; onSignedOut: () => void }) {
  const t = createTranslator(accountMessages, locale);

  return (
    <Menu
      label={t("account")}
      trigger={(props) => (
        <button type="button" className="appbar__avatar" aria-label={t("accountMenu", { name: me.name })} {...props}>
          {initialsOf(me.name)}
        </button>
      )}
    >
      <div className="appbar__who" role="presentation">
        <b>{me.name}</b>
        <span>{me.email}</span>
      </div>
      <MenuItem onSelect={onOpenAccount}>{t("accountAndSecurity")}</MenuItem>
      <MenuItem onSelect={() => void client.signOut().then(onSignedOut)}>{t("signOut")}</MenuItem>
    </Menu>
  );
}
