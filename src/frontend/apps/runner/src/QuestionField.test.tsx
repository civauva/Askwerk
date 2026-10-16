// SPDX-License-Identifier: AGPL-3.0-only
import type { AnswerValue } from "@askwerk/app-client";
import { createTranslator, type Question, type SurveyDefinition } from "@askwerk/schema";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { runnerMessages } from "./messages";
import { QuestionField } from "./QuestionField";

afterEach(cleanup);

const both = (de: string, en: string) => ({ de, en });
const t = createTranslator(runnerMessages, "en");

function field(question: Question, value?: AnswerValue, onChange: (value: AnswerValue | undefined) => void = () => undefined) {
  const definition: SurveyDefinition = { schemaVersion: 1, languages: ["de", "en"], defaultLanguage: "de", pages: [{ id: "p1", questions: [question] }] };

  return render(<QuestionField t={t} definition={definition} language="en" question={question} value={value} onChange={onChange} />);
}

const slider: Question = {
  id: "wait",
  variable: "WAIT",
  type: "slider",
  text: both("Wartezeit?", "Waiting time?"),
  required: false,
  scale: { min: 0, max: 60, step: 5, minLabel: both("Keine", "No wait"), maxLabel: both("Lang", "Long wait") },
};

describe("Slider (demo glitches, AWK-23)", () => {
  it("shows no fill before it is answered, and fills up to the answer", () => {
    field(slider);
    const range = () => screen.getByRole("slider");

    expect(range().style.getPropertyValue("--fill")).toBe("0%");
    cleanup();
    field(slider, { number: 15 });
    expect(range().style.getPropertyValue("--fill")).toBe("25%");
  });

  it("shows the numbers and the end labels at the ends (W31)", () => {
    const { container } = field(slider);

    expect([...container.querySelectorAll(".run-scale__ends span")].map((end) => end.textContent)).toEqual(["0 · No wait", "60 · Long wait"]);
  });

  it("asks to tap or drag before it is answered, with a tick per step, and is still unanswered (W31)", () => {
    const { container } = field(slider);

    expect(screen.getByText("Tap or drag to answer")).toBeTruthy();
    expect(container.querySelectorAll(".run-slider__ticks i")).toHaveLength(13);
    expect(screen.getByRole("slider").getAttribute("aria-valuetext")).toBe("Not answered yet");
    expect(screen.queryByRole("button", { name: "Clear answer" })).toBeNull();
  });

  it("clears an answer (W31)", () => {
    const onChange = vi.fn();
    field(slider, { number: 15 }, onChange);

    fireEvent.click(screen.getByRole("button", { name: "Clear answer" }));

    expect(onChange).toHaveBeenCalledWith(undefined);
  });

  it("shows the numbers at the ends when there are no labels", () => {
    const { container } = field({ ...slider, scale: { min: 0, max: 60, step: 5 } });

    expect([...container.querySelectorAll(".run-scale__ends span")].map((end) => end.textContent)).toEqual(["0", "60"]);
  });
});

describe("Faces (demo glitches, AWK-23)", () => {
  it("shows five colour emoji faces, none of them a plain-text symbol", () => {
    const { container } = field({ id: "clean", variable: "CLEAN", type: "rating", display: "faces", text: both("Sauber?", "Clean?"), required: false, scale: { min: 1, max: 5 } });
    const faces = [...container.querySelectorAll(".run-scale__step .run-face")].map((face) => face.textContent ?? "");

    expect(faces).toHaveLength(5);
    expect(faces.every((face) => /^[\u{1F600}-\u{1F64F}]$/u.test(face))).toBe(true);
  });
});
