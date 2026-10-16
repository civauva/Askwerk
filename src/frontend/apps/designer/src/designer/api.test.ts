// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveysClient, ThemesClient } from "@askwerk/app-client";
import { describe, expect, it } from "vitest";
import { designerApi } from "./api";
import { sampleDefinition } from "./testing";

describe("designerApi", () => {
  it("loads, saves and publishes through the surveys client", async () => {
    const calls: unknown[][] = [];
    const surveys: SurveysClient = {
      list: () => Promise.reject(new Error("not used")),
      create: () => Promise.reject(new Error("not used")),
      summary: () => Promise.reject(new Error("not used")),
      rename: () => Promise.reject(new Error("not used")),
      duplicate: () => Promise.reject(new Error("not used")),
      close: () => Promise.reject(new Error("not used")),
      reopen: () => Promise.reject(new Error("not used")),
      archive: () => Promise.reject(new Error("not used")),
      setClosingDate: () => Promise.reject(new Error("not used")),
      setOneResponse: () => Promise.reject(new Error("not used")),
      links: () => Promise.reject(new Error("not used")),
      approvals: () => Promise.reject(new Error("not used")),
      approve: () => Promise.reject(new Error("not used")),
      withdrawApproval: () => Promise.reject(new Error("not used")),
      look: () => Promise.reject(new Error("not used")),
      setLook: () => Promise.reject(new Error("not used")),
      dropLook: () => Promise.reject(new Error("not used")),
      listSamples: () => Promise.reject(new Error("not used")),
      createSample: () => Promise.reject(new Error("not used")),
      loadDraft: <T,>(id: string) => {
        calls.push(["loadDraft", id]);
        return Promise.resolve({ surveyId: id, title: "T", versionNumber: 2, definition: sampleDefinition as T, published: null });
      },
      saveDraft: (id, definition) => {
        calls.push(["saveDraft", id, definition]);
        return Promise.resolve();
      },
      publish: (id) => {
        calls.push(["publish", id]);
        return Promise.resolve({ versionNumber: 2, links: [{ kind: "system", hostname: "surveys.acme.at", isPrimary: true, url: "https://surveys.acme.at/s/kz26b7mq" }] });
      },
      setTheme: (id, themeId) => {
        calls.push(["setTheme", id, themeId]);
        return Promise.resolve();
      },
      listTemplates: () => Promise.reject(new Error("not used")),
      saveAsTemplate: () => Promise.reject(new Error("not used")),
      deleteTemplate: () => Promise.reject(new Error("not used")),
      editTemplate: () => Promise.reject(new Error("not used")),
      copyTemplate: () => Promise.reject(new Error("not used")),
    };
    const themes = {
      list: () => Promise.resolve([{ id: "t1", name: "Acme", isDefault: true, tokens: {} }]),
      upload: (file: File) => Promise.resolve({ id: "a1", kind: "image" as const, contentType: file.type, size: file.size, name: file.name }),
      assetUrl: (assetId: string) => `/api/app/assets/${assetId}`,
    } as unknown as ThemesClient;
    const api = designerApi(surveys, themes);

    const draft = await api.loadDraft("s1");
    await api.saveDraft("s1", sampleDefinition);
    const published = await api.publish("s1");

    expect(draft.definition).toEqual(sampleDefinition);
    expect(published.versionNumber).toBe(2);
    expect(calls).toEqual([["loadDraft", "s1"], ["saveDraft", "s1", sampleDefinition], ["publish", "s1"]]);
  });

  it("lists themes, chooses one, uploads an image and names smaller copies", async () => {
    const calls: unknown[][] = [];
    const surveys = { setTheme: (id: string, themeId: string | null) => (calls.push(["setTheme", id, themeId]), Promise.resolve()) } as unknown as SurveysClient;
    const themes = {
      list: () => Promise.resolve([{ id: "t1", name: "Acme", isDefault: true, tokens: {} }]),
      upload: (file: File) => Promise.resolve({ id: "a1", kind: "image" as const, contentType: file.type, size: file.size, name: file.name }),
      assetUrl: (assetId: string) => `/api/app/assets/${assetId}`,
    } as unknown as ThemesClient;
    const api = designerApi(surveys, themes);

    expect((await api.listThemes()).map(({ id, name, isDefault }) => ({ id, name, isDefault }))).toEqual([{ id: "t1", name: "Acme", isDefault: true }]);
    await api.setTheme("s1", null);
    expect(await api.uploadImage(new File(["x"], "shop.png", { type: "image/png" }))).toBe("a1");
    expect(calls).toEqual([["setTheme", "s1", null]]);
    expect(api.assetUrl("a1", 480)).toBe("/api/app/assets/a1?w=480");
  });
});
