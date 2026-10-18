// SPDX-License-Identifier: AGPL-3.0-only
import { RowTools, type RowTool } from "@askwerk/ui";
import { useState } from "react";
import type { ApiKeysMessageKey } from "./messages";

type T = (key: ApiKeysMessageKey, values?: Record<string, string | number>) => string;

export type MakeConnection = (kind: "api-key" | "oauth-client" | "webhook", id: string) => Promise<unknown>;

/**
 * A key's, client's or webhook's row actions as one toolbar (W42b B), led by "Make a connection of it" (W36) when the
 * page offers it: for one made before Connections joins them; once only.
 */
export function KeyRowTools({ t, kind, id, name, make, label, tools }: { t: T; kind: "api-key" | "oauth-client" | "webhook"; id: string; name: string; make: MakeConnection | undefined; label: string; tools: readonly RowTool[] }) {
  const [refused, setRefused] = useState(false);
  const connect: RowTool[] =
    make === undefined || refused ? [] : [{ icon: "plug", label: t("makeConnection"), name: t("makeConnectionOf", { name }), onClick: () => void make(kind, id).catch(() => setRefused(true)) }];

  return (
    <span className="api-keys__actions">
      {refused && <span className="api-keys__muted">{t("alreadyConnected")}</span>}
      <RowTools label={label} tools={[...connect, ...tools]} />
    </span>
  );
}
