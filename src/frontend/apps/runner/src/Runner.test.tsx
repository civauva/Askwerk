// SPDX-License-Identifier: AGPL-3.0-only
import { themePresets, type SurveyDefinition, type ThemeTokens } from "@askwerk/schema";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Runner } from "./Runner";
import { ApiError } from "@askwerk/app-client";
import { FakeRunClient, fieldErrors, sampleDefinition, traceId } from "./testing";

afterEach(cleanup);

function renderRunner(client = new FakeRunClient(), locale: "de" | "en" = "en") {
  const view = render(<Runner client={client} surveyId="s1" initialLocale={locale} reducedMotion />);

  return { client, ...view };
}

async function startSurvey(name = "Start the survey") {
  fireEvent.click(await screen.findByRole("button", { name }));
}

const question = (name: string) => screen.getByRole("group", { name: new RegExp(name) });

describe("Welcome page (W8 H)", () => {
  it("shows the welcome texts, the organisation, the length, and starts in the chosen language", async () => {
    const { client } = renderRunner();

    expect(await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" })).toBeTruthy();
    expect(screen.getByText("Thank you for taking part.")).toBeTruthy();
    expect(screen.getByText("Acme GmbH")).toBeTruthy();
    expect(screen.getByText("About 1 minute · 4 questions")).toBeTruthy();
    await startSurvey();

    expect(client.calls).toContainEqual(["start", "s1", "en"]);
    expect(await screen.findByText("Page 1 of 2 · Your visit")).toBeTruthy();
  });

  it("switches the survey language", async () => {
    renderRunner();

    fireEvent.click(await screen.findByRole("button", { name: /: (English|Deutsch)$/ }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: /Deutsch/ }));

    expect(await screen.findByRole("heading", { level: 1, name: "Kundenzufriedenheit 2026" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Umfrage starten" })).toBeTruthy();
  });
});

describe("Classic layout (W8 A, M4 A)", () => {
  it("saves each page with Next and finishes on the end page", async () => {
    const { client } = renderRunner();
    await startSurvey();

    fireEvent.click(within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^4/ }));
    fireEvent.click(within(question("How did you hear")).getByRole("checkbox", { name: "Other, please specify:" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Other, please specify:" }), { target: { value: "trade fair" } });
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    await waitFor(() => expect(client.calls).toContainEqual(["savePage", "s1", "p1", { q1: { number: 4 }, q2: { options: ["OTHER"], other: "trade fair" } }]));
    fireEvent.click(within(await screen.findByRole("group", { name: /Have you been here/ })).getByRole("radio", { name: /^No/ }));
    fireEvent.change(screen.getByRole("textbox", { name: "Your email address" }), { target: { value: "anna@acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("Thank you! Your answers are saved.")).toBeTruthy();
    expect(client.calls).toContainEqual(["savePage", "s1", "p2", { q3: { option: "0" }, q4: { text: "anna@acme.at" } }]);
    expect(client.calls.at(-1)).toEqual(["complete", "s1"]);
  });

  it("lists missing answers at the top, takes the focus there, and explains each at its question (DE)", async () => {
    const client = new FakeRunClient();
    client.saveAnswers = [fieldErrors({ q1: ["required"], q2: ["other-text-missing"] })];
    renderRunner(client, "de");
    await startSurvey("Umfrage starten");

    fireEvent.click(await screen.findByRole("button", { name: "Weiter" }));

    const summary = await screen.findByRole("alert");
    expect(within(summary).getByText("2 Antworten fehlen noch")).toBeTruthy();
    await waitFor(() => expect(document.activeElement).toBe(summary));
    expect(within(summary).getByRole("link", { name: "Wie zufrieden sind Sie?" })).toBeTruthy();
    expect(within(question("Wie zufrieden")).getByText("Bitte wählen Sie einen Wert von 1 bis 5.")).toBeTruthy();
    expect(within(question("Wie haben Sie")).getByText("Bitte geben Sie an, was „Sonstiges“ ist.")).toBeTruthy();
  });

  it("goes back without losing answers", async () => {
    renderRunner();
    await startSurvey();
    fireEvent.click(within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^2/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    fireEvent.click(await screen.findByRole("button", { name: "Back" }));

    expect((within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^2/ }) as HTMLInputElement).checked).toBe(true);
  });

  it("chooses an option with its number key", async () => {
    renderRunner();
    await startSurvey();
    const satisfaction = await screen.findByRole("group", { name: /How satisfied/ });

    fireEvent.keyDown(satisfaction, { key: "5" });

    expect((within(satisfaction).getByRole("radio", { name: /^5/ }) as HTMLInputElement).checked).toBe(true);
  });

  it("has no accessibility violations", async () => {
    const { container } = renderRunner();
    await startSurvey();
    await screen.findByRole("group", { name: /How satisfied/ });

    await expectNoAxeViolations(container);
  });
});

const oneAtATime: SurveyDefinition = { ...sampleDefinition, layout: "one-at-a-time" };

describe("One-at-a-time layout (W8 D, M4 D)", () => {
  it("shows one question, stays on it after Ja with J until the respondent confirms, and saves the page at its end", async () => {
    const client = new FakeRunClient(oneAtATime, { status: "partial", pageId: "p2", language: "de", answers: {} });
    renderRunner(client, "de");
    fireEvent.click(await screen.findByRole("button", { name: "Fortsetzen" }));

    const yesNo = await screen.findByRole("group", { name: /Waren Sie schon einmal hier/ });
    expect(screen.getByText("Frage 3 von 4")).toBeTruthy();
    fireEvent.keyDown(yesNo, { key: "j" });

    // No moving on by itself (owner, 2026-10-15): the answer is chosen, the question stays.
    await new Promise((resolve) => setTimeout(resolve, 600));
    expect(screen.getByRole("group", { name: /Waren Sie schon einmal hier/ })).toBeTruthy();
    expect(screen.queryByRole("group", { name: /Ihre E-Mail-Adresse/ })).toBeNull();
    fireEvent.keyDown(yesNo, { key: "Enter" });

    expect(await screen.findByRole("group", { name: /Ihre E-Mail-Adresse/ })).toBeTruthy();
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Ihre E-Mail-Adresse" }), { key: "Enter" });

    await waitFor(() => expect(client.calls).toContainEqual(["savePage", "s1", "p2", { q3: { option: "1" } }]));
  });

  it("goes back with Shift+Enter", async () => {
    const client = new FakeRunClient(oneAtATime);
    renderRunner(client);
    await startSurvey();
    fireEvent.click(within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^3/ }));
    fireEvent.click(screen.getByRole("button", { name: /^OK/ }));
    await screen.findByRole("group", { name: /How did you hear/ });

    fireEvent.keyDown(question("How did you hear"), { key: "Enter", shiftKey: true });

    expect(await screen.findByRole("group", { name: /How satisfied/ })).toBeTruthy();
    expect(screen.getByText("Question 1 of 4")).toBeTruthy();
  });

  it("uses Y and N in English", async () => {
    const client = new FakeRunClient(oneAtATime, { status: "partial", pageId: "p2", language: "en", answers: {} });
    renderRunner(client);
    fireEvent.click(await screen.findByRole("button", { name: "Continue" }));

    const yesNo = await screen.findByRole("group", { name: /Have you been here/ });
    fireEvent.keyDown(yesNo, { key: "n" });
    expect((within(yesNo).getByRole("radio", { name: /^No/ }) as HTMLInputElement).checked).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: /^OK/ }));

    expect(await screen.findByRole("group", { name: /Your email address/ })).toBeTruthy();
  });
});

describe("Other screens (W8 F)", () => {
  it("welcomes a respondent back with the answers so far", async () => {
    const client = new FakeRunClient(sampleDefinition, { status: "partial", pageId: "p1", language: "en", answers: { q1: { number: 5 } } });
    renderRunner(client);

    expect(await screen.findByRole("heading", { name: "Welcome back" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect((within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^5/ }) as HTMLInputElement).checked).toBe(true);
  });

  it("says a respondent who has finished has already answered (one response per person, W24 C3)", async () => {
    renderRunner(new FakeRunClient(sampleDefinition, { status: "complete", pageId: "p2", language: "en", answers: {} }));

    expect(await screen.findByRole("heading", { name: "You have already answered" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Answer again" })).toBeNull();
  });

  it("shows the end page again with Answer again when more than one response is allowed (W24 C4)", async () => {
    const client = new FakeRunClient(sampleDefinition, { status: "complete", pageId: "p2", language: "en", answers: {} });
    client.change({ oneResponsePerPerson: false });
    renderRunner(client);

    expect(await screen.findByText("Thank you! Your answers are saved.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Answer again" }));

    await screen.findByText("Page 1 of 2 · Your visit");
    expect(client.calls.filter(([call]) => call === "start")).toHaveLength(1);
  });

  it("offers Answer again right after finishing when more than one response is allowed", async () => {
    const client = new FakeRunClient();
    client.change({ oneResponsePerPerson: false });
    renderRunner(client);
    await startSurvey();
    fireEvent.click(within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^5/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    fireEvent.click(within(await screen.findByRole("group", { name: /Have you been here/ })).getByRole("radio", { name: /^No/ }));
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByRole("button", { name: "Answer again" })).toBeTruthy();
  });

  it("says when a survey has closed", async () => {
    renderRunner(new FakeRunClient(sampleDefinition, null, "closed"), "de");

    expect(await screen.findByRole("heading", { name: "Diese Umfrage ist geschlossen" })).toBeTruthy();
  });

  it("says on which day a survey closed by its closing date, in the organisation's time zone (W24 C1, C2)", async () => {
    const client = new FakeRunClient(sampleDefinition, null, "closed");
    client.change({ closedAt: "2026-10-31T23:30:00Z", timeZone: "Europe/Vienna" });
    renderRunner(client, "de");

    expect(await screen.findByText("Sie wurde am 1. November 2026 geschlossen.")).toBeTruthy();
  });

  it("shows the closed page when the survey closes while answering", async () => {
    const client = new FakeRunClient();
    client.saveAnswers = [new ApiError(409, traceId, "closed", { code: "closed" })];
    renderRunner(client);
    await startSurvey();
    fireEvent.click(within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^5/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(await screen.findByRole("heading", { name: "This survey has closed" })).toBeTruthy();
  });

  it("says when a link leads nowhere", async () => {
    const client = new FakeRunClient();
    client.survey = null;
    renderRunner(client);

    expect(await screen.findByRole("heading", { name: "Survey not found" })).toBeTruthy();
  });
});

describe("Preview (AWK-22, W9 B and C)", () => {
  it("says on every screen that answers are not saved, in the respondent's language", async () => {
    render(<Runner client={new FakeRunClient()} surveyId="s1" initialLocale="de" reducedMotion preview />);

    expect((await screen.findByRole("note")).textContent).toBe("Vorschau · Antworten werden nicht gespeichert");
    await startSurvey("Umfrage starten");
    await screen.findByText("Seite 1 von 2 · Ihr Besuch");
    expect(screen.getByRole("note").textContent).toBe("Vorschau · Antworten werden nicht gespeichert");
  });

  it("shows no banner to respondents", async () => {
    renderRunner();

    await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });
    expect(screen.queryByRole("note")).toBeNull();
  });
});

describe("Welcome page switched off (AWK-22, W9 A)", () => {
  const withoutWelcome: SurveyDefinition = { ...sampleDefinition, welcome: { ...sampleDefinition.welcome, enabled: false } };

  it("opens on the first page and starts the response only with the first Next", async () => {
    const { client } = renderRunner(new FakeRunClient(withoutWelcome));

    expect(await screen.findByText("Page 1 of 2 · Your visit")).toBeTruthy();
    expect(client.calls.map(([name]) => name)).toEqual(["open"]);
    fireEvent.click(within(question("How satisfied")).getByRole("radio", { name: /^4/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    await waitFor(() => expect(client.calls.map(([name]) => name)).toEqual(["open", "start", "savePage"]));
  });
});

describe("Length on the welcome page", () => {
  it("speaks of one question in the singular", async () => {
    const single: SurveyDefinition = { ...sampleDefinition, pages: [{ id: "p1", questions: [sampleDefinition.pages[0]!.questions[0]!] }] };
    render(<Runner client={new FakeRunClient(single)} surveyId="s1" initialLocale="de" reducedMotion />);

    expect(await screen.findByText("Etwa 1 Minute · 1 Frage")).toBeTruthy();
  });
});

describe("Theme (AWK-17 step 3, W10 E)", () => {
  const werkstatt = themePresets[0]!.tokens;
  const branded: ThemeTokens = {
    ...werkstatt,
    colours: { ...werkstatt.colours, accent: "#0b6e4f", background: "#fbf3ea" },
    fonts: { heading: { family: "Acme Sans", asset: "f1" }, body: { family: "IBM Plex Sans" } },
    corners: "rounded",
    controls: "pills",
    layout: "header-image",
    progress: "none",
    logo: { asset: "a1" },
    headerImage: { asset: "h1", focalX: 0.5, focalY: 0.25 },
    background: { kind: "image-overlay", image: { asset: "b1" }, fit: "cover", overlay: "#ffffff", overlayOpacity: 0.8 },
  };
  const page = () => document.querySelector<HTMLElement>(".run")!;

  async function themed(theme: ThemeTokens = branded) {
    render(<Runner client={new FakeRunClient(sampleDefinition, null, "open", theme)} surveyId="s1" initialLocale="en" reducedMotion />);
    await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });
  }

  it("draws the page in the theme's colours, fonts and corners", async () => {
    await themed();

    expect(page().style.getPropertyValue("--aw-signal")).toBe("#0b6e4f");
    expect(page().style.getPropertyValue("--aw-paper")).toBe("#fbf3ea");
    expect(page().style.getPropertyValue("--aw-font-display")).toContain('"Acme Sans"');
    expect([page().dataset.corners, page().dataset.controls, page().dataset.layout]).toEqual(["rounded", "pills", "header-image"]);
  });

  it("loads an uploaded font from the survey's files", async () => {
    await themed();

    // The font face is added by an effect; under a full parallel run it can come a moment after the heading.
    await waitFor(() => expect(document.head.textContent ?? "").toContain('font-family: "Acme Sans"'));
    expect(document.head.textContent ?? "").toContain("/api/app/run/s1/assets/f1");
  });

  it("shows the logo, named after the organisation, instead of its initial", async () => {
    await themed();

    const logo = screen.getByRole("img", { name: "Acme GmbH" });
    expect(logo.getAttribute("src")).toBe("/api/app/run/s1/assets/a1?w=480");
  });

  it("shows the header image in sizes for phones and desktops, around its focal point", async () => {
    await themed();

    const header = document.querySelector<HTMLImageElement>(".run-hero img")!;
    expect(header.getAttribute("alt")).toBe("");
    expect(header.getAttribute("srcset")).toBe("/api/app/run/s1/assets/h1?w=960 960w, /api/app/run/s1/assets/h1?w=1600 1600w, /api/app/run/s1/assets/h1?w=2400 2400w");
    expect(header.style.objectPosition).toBe("50% 25%");
  });

  it("lays a background image under its overlay", async () => {
    await themed();

    expect(page().style.backgroundImage).toContain("rgba(255, 255, 255, 0.8)");
    expect(page().style.backgroundImage).toContain("/api/app/run/s1/assets/b1?w=1600");
  });

  it("keeps the Werkstatt look when the survey has no theme", async () => {
    render(<Runner client={new FakeRunClient()} surveyId="s1" initialLocale="en" reducedMotion />);
    await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });

    expect(page().style.getPropertyValue("--aw-signal")).toBe("");
    expect(screen.getByText("Acme GmbH")).toBeTruthy();
  });

  it("hides the progress bar but still tells the page", async () => {
    await themed();
    await startSurvey();

    expect(await screen.findByText("Page 1 of 2 · Your visit")).toBeTruthy();
    expect(document.querySelector(".run-ruler")).toBeNull();
  });

  it("shows the survey's own header image instead of the theme's, with its alt text", async () => {
    const own: SurveyDefinition = { ...sampleDefinition, headerImage: { asset: "s9", alt: { de: "Unsere Filiale", en: "Our shop" } } };
    render(<Runner client={new FakeRunClient(own, null, "open", { ...werkstatt, layout: "card" })} surveyId="s1" initialLocale="en" reducedMotion />);
    await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });

    const header = screen.getByRole("img", { name: "Our shop" });
    expect(header.getAttribute("src")).toBe("/api/app/run/s1/assets/s9?w=1600");
  });

  it("has no accessibility violations in a theme", async () => {
    await themed();

    await expectNoAxeViolations(document.body);
  });

  it("draws every look of the gallery without accessibility violations (W26)", { timeout: 30000 }, async () => {
    for (const preset of themePresets) {
      const { unmount } = render(<Runner client={new FakeRunClient(sampleDefinition, null, "open", preset.tokens)} surveyId="s1" initialLocale="en" reducedMotion />);
      await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });
      await expectNoAxeViolations(document.body);
      unmount();
    }
  });

  it("sets the finer settings as data attributes, draws the pattern and real steps (W26)", async () => {
    const konfetti = themePresets.find((preset) => preset.id === "konfetti")!.tokens;
    await themed(konfetti);

    expect([page().dataset.columns, page().dataset.selected, page().dataset.welcome, page().dataset.pattern, page().dataset.buttonWidth]).toEqual(["two", "fill", "band", "confetti", "wide"]);
    expect(document.querySelector(".run-pattern--confetti")).not.toBeNull();
    await startSurvey();
    await screen.findByText("Page 1 of 2 · Your visit");
    expect(document.querySelectorAll(".run-steps i")).toHaveLength(2);
    expect(document.querySelectorAll(".run-steps i.run-steps__on")).toHaveLength(1);
  });

  it("puts the questions on a frosted panel over the mountains, with the title over the picture (W28)", async () => {
    const panorama = themePresets.find((preset) => preset.id === "panorama")!.tokens;
    await themed(panorama);

    expect([page().dataset.pattern, page().dataset.panel, page().dataset.title, page().dataset.textBlock]).toEqual(["mountains", "frosted", "over-picture", "tinted"]);
    expect(document.querySelector(".run-pattern")).toBeNull();
    await startSurvey();
    await screen.findAllByText("Page 1 of 2 · Your visit");
    expect(document.querySelector(".run-overtitle__title")?.textContent).toBe("Customer satisfaction 2026");
    expect(document.querySelector(".run-overtitle")?.getAttribute("aria-hidden")).toBe("true");
    await expectNoAxeViolations(document.body);
  });

  it("shows the dark colours when the look is dark (W26)", async () => {
    const nacht = themePresets.find((preset) => preset.id === "nacht")!.tokens;
    await themed(nacht);

    expect(page().dataset.mode).toBe("dark");
    expect(page().style.getPropertyValue("--aw-paper")).toBe(nacht.darkColours!.background);
  });

  it("goes where the look being changed shows, in the preview only (W26)", async () => {
    render(<Runner liveLook client={new FakeRunClient(sampleDefinition, null, "open", werkstatt)} surveyId="s1" initialLocale="en" reducedMotion />);
    await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });

    act(() => window.dispatchEvent(new MessageEvent("message", { origin: window.location.origin, data: { type: "askwerk:goto", target: "yes-no" } })));
    expect(await screen.findByText(/Page 2 of 2/)).toBeTruthy();
    act(() => window.dispatchEvent(new MessageEvent("message", { origin: window.location.origin, data: { type: "askwerk:goto", target: "end" } })));
    expect(document.querySelector(".run-message h1")).not.toBeNull();
  });

  it("tells the designer's Preview where it is, for Page 2 of 2 in the stage bar (W32)", async () => {
    const sent: unknown[] = [];
    const parent = { postMessage: (message: unknown) => sent.push(message) };
    Object.defineProperty(window, "parent", { value: parent, configurable: true });

    try {
      render(<Runner liveLook client={new FakeRunClient(sampleDefinition, null, "open", werkstatt)} surveyId="s1" initialLocale="en" reducedMotion />);
      await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });
      expect(sent).toContainEqual({ type: "askwerk:preview-page", at: "welcome", pages: 2 });

      act(() => window.dispatchEvent(new MessageEvent("message", { origin: window.location.origin, data: { type: "askwerk:goto", target: "yes-no" } })));
      await waitFor(() => expect(sent).toContainEqual({ type: "askwerk:preview-page", at: 2, pages: 2 }));
    } finally {
      Object.defineProperty(window, "parent", { value: window, configurable: true });
    }
  });

  it("takes the look being edited from the designer in the preview, from its own origin only (W26)", async () => {
    render(<Runner liveLook client={new FakeRunClient(sampleDefinition, null, "open", werkstatt)} surveyId="s1" initialLocale="en" reducedMotion />);
    await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" });
    const graphit = themePresets.find((preset) => preset.id === "graphit")!.tokens;

    act(() => window.dispatchEvent(new MessageEvent("message", { origin: "https://elsewhere.example", data: { type: "askwerk:look", tokens: graphit } })));
    expect(page().style.getPropertyValue("--aw-signal")).toBe(werkstatt.colours.accent);
    act(() => window.dispatchEvent(new MessageEvent("message", { origin: window.location.origin, data: { type: "askwerk:look", tokens: graphit } })));

    expect(page().style.getPropertyValue("--aw-signal")).toBe(graphit.colours.accent);
    expect(page().dataset.marker).toBe("check");
  });
});

describe("Images in the survey (AWK-17 step 4b, W10 F1)", () => {
  const withImages: SurveyDefinition = {
    ...sampleDefinition,
    pages: [
      {
        id: "p1",
        questions: [
          { id: "i1", variable: "IMG", type: "image", text: { de: "Eröffnet im Mai", en: "Opened in May" }, required: false, image: { asset: "a1", alt: { de: "Filiale", en: "Our shop" }, size: "half", align: "right" } },
          {
            id: "q1",
            variable: "STORE",
            type: "single",
            text: { de: "Welche Filiale?", en: "Which branch?" },
            required: false,
            image: { asset: "a2", alt: { de: "Karte", en: "Map" } },
            options: [
              { code: "1", label: { de: "Wien", en: "Vienna" }, image: { asset: "a3" } },
              { code: "2", label: { de: "Graz", en: "Graz" }, image: { asset: "a4" } },
            ],
          },
        ],
      },
    ],
  };

  async function onFirstPage() {
    render(<Runner client={new FakeRunClient(withImages)} surveyId="s1" initialLocale="en" reducedMotion />);
    await startSurvey();
    await screen.findByRole("group", { name: /Which branch/ });
  }

  it("shows an image block with its alt text, caption, size and alignment, in sizes for the screen", async () => {
    await onFirstPage();

    const image = screen.getByRole("img", { name: "Our shop" });
    expect(image.getAttribute("srcset")).toBe("/api/app/run/s1/assets/a1?w=480 480w, /api/app/run/s1/assets/a1?w=960 960w, /api/app/run/s1/assets/a1?w=1600 1600w");
    expect(image.closest("figure")?.className).toBe("run-figure run-figure--half run-figure--right");
    expect(screen.getByText("Opened in May").tagName).toBe("FIGCAPTION");
  });

  it("shows a question's image with its alt text", async () => {
    await onFirstPage();

    expect(screen.getByRole("img", { name: "Map" }).getAttribute("src")).toBe("/api/app/run/s1/assets/a2?w=960");
  });

  it("shows picture choices that are answered by their label", async () => {
    await onFirstPage();

    const vienna = within(question("Which branch")).getByRole("radio", { name: "Vienna" });
    expect(vienna.closest("label")?.querySelector("img")?.getAttribute("src")).toBe("/api/app/run/s1/assets/a3?w=480");
    expect(vienna.closest("label")?.querySelector("img")?.getAttribute("alt")).toBe("");
  });

  it("does not count image blocks as questions", async () => {
    render(<Runner client={new FakeRunClient(withImages)} surveyId="s1" initialLocale="en" reducedMotion />);

    expect(await screen.findByText("About 1 minute · 1 question")).toBeTruthy();
  });

  it("has no accessibility violations with images", async () => {
    await onFirstPage();

    await expectNoAxeViolations(document.body);
  });
});

describe("Answered in the email (AWK-17 step 6, W10 E)", () => {
  it("opens the survey with the answer clicked in the email, past the welcome page", async () => {
    const client = new FakeRunClient();
    render(<Runner client={client} surveyId="s1" initialLocale="en" reducedMotion initialAnswer={{ questionId: "q1", value: "4" }} />);

    expect(await screen.findByText("Page 1 of 2 · Your visit")).toBeTruthy();
    expect((within(question("How satisfied")).getByRole("radio", { name: /^4/ }) as HTMLInputElement).checked).toBe(true);
    expect(client.calls.map(([name]) => name)).toEqual(["open", "start"]);
  });

  it("ignores an answer that does not fit the question", async () => {
    render(<Runner client={new FakeRunClient()} surveyId="s1" initialLocale="en" reducedMotion initialAnswer={{ questionId: "q1", value: "99" }} />);

    expect(await screen.findByRole("heading", { level: 1, name: "Customer satisfaction 2026" })).toBeTruthy();
  });
});

describe("Logic (AWK-31, W19)", () => {
  const withLogic = {
    ...sampleDefinition,
    pages: [
      {
        ...sampleDefinition.pages[0]!,
        questions: [
          sampleDefinition.pages[0]!.questions[0]!,
          { ...sampleDefinition.pages[0]!.questions[1]!, showWhen: { match: "all" as const, conditions: [{ question: "q1", op: "at-most" as const, number: 2 }] } },
        ],
        after: { when: { match: "all" as const, conditions: [{ question: "q1", op: "is" as const, number: 5 }] }, end: true },
      },
      sampleDefinition.pages[1]!,
    ],
  };

  it("shows a question once an earlier answer asks for it, and sends only shown answers", async () => {
    const client = new FakeRunClient(withLogic);
    renderRunner(client);
    await startSurvey();

    expect(screen.queryByRole("group", { name: /How did you hear/ })).toBeNull();
    fireEvent.click(within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^2/ }));
    fireEvent.click(within(question("How did you hear")).getByRole("checkbox", { name: "Search engine" }));
    fireEvent.click(within(question("How satisfied")).getByRole("radio", { name: /^3/ }));
    expect(screen.queryByRole("group", { name: /How did you hear/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    await waitFor(() => expect(client.calls).toContainEqual(["savePage", "s1", "p1", { q1: { number: 3 } }]));
  });

  it("ends after a page when its rule holds: that page sends the survey", async () => {
    const client = new FakeRunClient(withLogic);
    renderRunner(client);
    await startSurvey();

    fireEvent.click(within(await screen.findByRole("group", { name: /How satisfied/ })).getByRole("radio", { name: /^5/ }));
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("Thank you! Your answers are saved.")).toBeTruthy();
    expect(client.calls.at(-1)).toEqual(["complete", "s1"]);
  });
});
