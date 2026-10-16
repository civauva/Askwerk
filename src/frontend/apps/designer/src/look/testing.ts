// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written ThemesClient for the Look page's tests: themes in memory, calls recorded.
import type { StoredAsset, StoredTheme, ThemesClient } from "@askwerk/app-client";
import { themePresets, type ThemeTokens } from "@askwerk/schema";

export const werkstatt = themePresets.find((preset) => preset.id === "werkstatt")!.tokens;

export const acmeStandard: StoredTheme<ThemeTokens> = { id: "t1", name: "Acme Standard", isDefault: true, tokens: werkstatt };
export const acmeEvent: StoredTheme<ThemeTokens> = { id: "t2", name: "Acme Event", isDefault: false, tokens: { ...werkstatt, corners: "rounded" } };

export class FakeThemesClient implements ThemesClient {
  readonly calls: [string, ...unknown[]][] = [];
  private next = 10;

  constructor(public themes: StoredTheme<ThemeTokens>[] = [acmeStandard, acmeEvent]) {}

  list = <T,>() => {
    this.calls.push(["list"]);
    return Promise.resolve(this.themes.map((theme) => ({ ...theme })) as StoredTheme<T>[]);
  };

  create = (name: string, tokens: unknown) => {
    this.calls.push(["create", name, tokens]);
    const id = `t${this.next++}`;
    this.themes = [...this.themes, { id, name, isDefault: this.themes.length === 0, tokens: tokens as ThemeTokens }];
    return Promise.resolve(id);
  };

  update = (themeId: string, name: string, tokens: unknown) => {
    this.calls.push(["update", themeId, name, tokens]);
    this.themes = this.themes.map((theme) => (theme.id === themeId ? { ...theme, name, tokens: tokens as ThemeTokens } : theme));
    return Promise.resolve();
  };

  makeDefault = (themeId: string) => {
    this.calls.push(["makeDefault", themeId]);
    this.themes = this.themes.map((theme) => ({ ...theme, isDefault: theme.id === themeId }));
    return Promise.resolve();
  };

  remove = (themeId: string) => {
    this.calls.push(["remove", themeId]);
    this.themes = this.themes.filter((theme) => theme.id !== themeId);
    return Promise.resolve();
  };

  upload = (file: File, kind: "image" | "font") => {
    this.calls.push(["upload", file.name, kind]);
    return Promise.resolve<StoredAsset>({ id: `a${this.next++}`, kind, contentType: file.type, size: file.size, name: file.name });
  };

  assetUrl = (assetId: string) => `/api/app/assets/${assetId}`;

  /** The last update sent for a theme. */
  lastUpdate(themeId: string): { name: string; tokens: ThemeTokens } | undefined {
    const call = this.calls.filter(([name, id]) => name === "update" && id === themeId).at(-1);
    return call === undefined ? undefined : { name: call[2] as string, tokens: call[3] as ThemeTokens };
  }
}
