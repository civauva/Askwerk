// SPDX-License-Identifier: AGPL-3.0-only
import { useState } from "react";
import type { ApiKeysMessageKey } from "./messages";

type T = (key: ApiKeysMessageKey, values?: Record<string, string | number>) => string;

export type MakeConnection = (kind: "api-key" | "oauth-client" | "webhook", id: string) => Promise<unknown>;

/** "Make a connection of it" (W36): a key, client or webhook made before Connections joins them; once only. */
export function MakeConnectionButton({ t, kind, id, name, make }: { t: T; kind: "api-key" | "oauth-client" | "webhook"; id: string; name: string; make: MakeConnection }) {
  const [refused, setRefused] = useState(false);

  return refused ? (
    <span className="api-keys__muted">{t("alreadyConnected")}</span>
  ) : (
    <button type="button" className="api-keys__revoke" aria-label={t("makeConnectionOf", { name })} onClick={() => void make(kind, id).catch(() => setRefused(true))}>
      {t("makeConnection")}
    </button>
  );
}
