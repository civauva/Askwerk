// SPDX-License-Identifier: AGPL-3.0-only
// Images in a survey (AWK-17 step 4b, W10 F1): image blocks, an image with a question, picture choices.
import type { Question, SurveyDefinition } from "@askwerk/schema";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Designer } from "./Designer";
import { FakeDesignerApi, sampleDraft, sequentialIds } from "./testing";

afterEach(cleanup);

const png = (name: string) => new File([new Uint8Array([137, 80, 78, 71])], name, { type: "image/png" });

function draftWith(...questions: Question[]) {
  const definition: SurveyDefinition = { schemaVersion: 1, languages: ["de", "en"], defaultLanguage: "en", pages: [{ id: "p1", questions }] };

  return new FakeDesignerApi({ ...sampleDraft, definition });
}

async function open(api: FakeDesignerApi, select?: string) {
  const view = render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} />);
  await screen.findByRole("navigation", { name: "Outline" });
  await act(async () => {});

  if (select !== undefined) {
    fireEvent.click(within(screen.getByRole("navigation", { name: "Outline" })).getByRole("button", { name: new RegExp(select) }));
  }

  return view;
}

const inspector = () => within(screen.getByRole("complementary", { name: "Question settings" }));
const canvas = () => within(screen.getByRole("main", { name: "Canvas" }));
const saved = (api: FakeDesignerApi) => api.saved.at(-1)?.pages[0]?.questions;
const single: Question = {
  id: "q1",
  variable: "STORE",
  type: "single",
  text: { en: "Which branch?" },
  required: false,
  options: [
    { code: "1", label: { en: "Vienna" } },
    { code: "2", label: { en: "Graz" } },
  ],
};

describe("Image block (W10 F1)", () => {
  it("is added from the type menu and takes its image on the card", async () => {
    const api = draftWith(single);
    await open(api);

    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);
    fireEvent.click(screen.getByRole("option", { name: /^Image/ }));
    fireEvent.change(canvas().getByLabelText("Image file"), { target: { files: [png("shop.png")] } });

    await waitFor(() => expect(saved(api)?.at(-1)).toMatchObject({ type: "image", required: false, image: { asset: "a1" } }));
    expect(api.uploads).toEqual(["shop.png"]);
    // No alt text yet, so the image is not announced; the inspector asks for one.
    expect(document.querySelector(".designer-image-block img")?.getAttribute("src")).toBe("/api/app/assets/a1?w=960");
  });

  it("sets alt text, caption, size and alignment", async () => {
    const api = draftWith({ id: "i1", variable: "IMG_SHOP", type: "image", text: {}, required: false, image: { asset: "a7" } });
    await open(api, "IMG_SHOP");

    fireEvent.change(inspector().getByRole("textbox", { name: "Alt text (EN)" }), { target: { value: "Our shop in Vienna" } });
    fireEvent.change(canvas().getByRole("textbox", { name: "Caption" }), { target: { value: "Opened in May" } });
    fireEvent.click(within(inspector().getByRole("group", { name: "Size" })).getByRole("radio", { name: "Half" }));
    fireEvent.click(within(inspector().getByRole("group", { name: "Align" })).getByRole("radio", { name: "Right" }));

    await waitFor(() =>
      expect(saved(api)?.[0]).toMatchObject({ text: { en: "Opened in May" }, image: { asset: "a7", alt: { en: "Our shop in Vienna" }, size: "half", align: "right" } }),
    );
    expect(inspector().queryByRole("switch", { name: "Required" })).toBeNull();
  });
});

describe("Image with a question (W10 F1)", () => {
  it("adds an image with alt text to any question, and removes it", async () => {
    const api = draftWith(single);
    await open(api, "STORE");

    fireEvent.change(inspector().getByLabelText("Question image file"), { target: { files: [png("map.png")] } });
    await waitFor(() => expect(saved(api)?.[0]?.image).toEqual({ asset: "a1" }));
    fireEvent.change(inspector().getByRole("textbox", { name: "Alt text (EN)" }), { target: { value: "Map of our branches" } });
    await waitFor(() => expect(saved(api)?.[0]?.image).toEqual({ asset: "a1", alt: { en: "Map of our branches" } }));
    fireEvent.click(inspector().getByRole("button", { name: "Remove image" }));

    await waitFor(() => expect(saved(api)?.[0]?.image).toBeUndefined());
  });
});

describe("Picture choices (W10 F1)", () => {
  it("gives an option a picture, which its label describes", async () => {
    const api = draftWith(single);
    await open(api, "STORE");

    fireEvent.change(inspector().getByLabelText("Picture for option 1"), { target: { files: [png("vienna.png")] } });

    await waitFor(() => expect(saved(api)?.[0]?.options?.[0]).toEqual({ code: "1", label: { en: "Vienna" }, image: { asset: "a1" } }));
    fireEvent.click(inspector().getByRole("button", { name: "Remove picture for option 1" }));
    await waitFor(() => expect(saved(api)?.[0]?.options?.[0]).toEqual({ code: "1", label: { en: "Vienna" } }));
  });

  it("has no accessibility violations with images", async () => {
    const { container } = await open(
      draftWith({ ...single, image: { asset: "a3", alt: { en: "Map" } }, options: [{ code: "1", label: { en: "Vienna" }, image: { asset: "a4" } }, { code: "2", label: { en: "Graz" } }] }),
      "STORE",
    );

    await expectNoAxeViolations(container);
  });
});
