// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** A theme as stored; the tokens stay opaque here, packages/schema types them. */
export interface StoredTheme<TTokens = unknown> {
  readonly id: string;
  readonly name: string;
  readonly isDefault: boolean;
  readonly tokens: TTokens;
}

export interface StoredAsset {
  readonly id: string;
  readonly kind: "image" | "font";
  readonly contentType: string;
  readonly size: number;
  readonly name: string;
  /** Images: pixels. */
  readonly width?: number | null;
  readonly height?: number | null;
}

/** One of Askwerk's background pictures (AWK-74): a public-domain photo with its topic and source. */
export interface AskwerkPicture {
  readonly id: string;
  readonly topic: string;
  readonly title: string;
  readonly creator?: string | null;
  readonly license: string;
  readonly source: string;
  readonly page: string;
  readonly width: number;
  readonly height: number;
}

/** Themes and uploaded files of the current organisation (AWK-17, W10 A). Errors are ApiError with the check's findings. */
export interface ThemesClient {
  list<TTokens = unknown>(): Promise<StoredTheme<TTokens>[]>;
  create(name: string, tokens: unknown): Promise<string>;
  update(themeId: string, name: string, tokens: unknown): Promise<void>;
  makeDefault(themeId: string): Promise<void>;
  remove(themeId: string): Promise<void>;
  upload(file: File, kind: "image" | "font"): Promise<StoredAsset>;
  /** Where the browser loads an uploaded file from (signed in). */
  assetUrl(assetId: string): string;
  /** Askwerk's background pictures by topic (AWK-74). */
  pictures(): Promise<{ topics: string[]; items: AskwerkPicture[] }>;
  /** Copies one into the organisation's files; the new file's id. */
  usePicture(pictureId: string): Promise<string>;
  /** Its thumbnail for the picture panel. */
  pictureThumbnailUrl(pictureId: string): string;
}

export function createThemesClient(options: AppClientOptions): ThemesClient {
  const request = createRequest(options);
  const at = (themeId: string) => `/api/app/themes/${encodeURIComponent(themeId)}`;

  return {
    list: <TTokens>() => request<{ items: StoredTheme<TTokens>[] }>("GET", "/api/app/themes").then((answer) => answer.items),
    create: (name, tokens) => request<{ id: string }>("POST", "/api/app/themes", { name, tokens }).then((created) => created.id),
    update: (themeId, name, tokens) => request("PUT", at(themeId), { name, tokens }),
    makeDefault: (themeId) => request("POST", `${at(themeId)}/default`),
    remove: (themeId) => request("DELETE", at(themeId)),
    upload: (file, kind) => {
      const form = new FormData();
      form.append("file", file);
      form.append("kind", kind);

      return request<StoredAsset>("POST", "/api/app/assets", form);
    },
    assetUrl: (assetId) => `/api/app/assets/${encodeURIComponent(assetId)}`,
    pictures: () => request<{ topics: string[]; items: AskwerkPicture[] }>("GET", "/api/app/pictures"),
    usePicture: (pictureId) => request<{ assetId: string }>("POST", `/api/app/pictures/${encodeURIComponent(pictureId)}/use`).then((used) => used.assetId),
    pictureThumbnailUrl: (pictureId) => `/api/app/pictures/${encodeURIComponent(pictureId)}/thumbnail`,
  };
}
