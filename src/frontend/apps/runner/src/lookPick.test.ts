// SPDX-License-Identifier: AGPL-3.0-only
import { afterEach, describe, expect, it } from "vitest";
import { pickAt, startPicking } from "./lookPick";

afterEach(() => {
  document.body.innerHTML = "";
});

describe("lookPick (W44 A, AWK-69): the preview as a map of its parts", () => {
  it("names the part under the pointer, the most specific first", () => {
    document.body.innerHTML = `<div class="run"><header class="run-head"><span class="run-org">Acme</span></header><main class="run-main">
      <fieldset class="run-q"><legend class="run-q__title">Where?</legend><div class="run-choices"><label class="run-choice"><span id="c">Car</span></label></div>
      <p class="run-q__error" id="e">Choose one</p></fieldset><div class="run-nav"><button class="run-button" id="b">Next</button></div><p id="m">Room</p></main></div>`;
    const at = (selector: string) => pickAt(document.querySelector(selector))?.pick;

    expect(at("#c")).toEqual({ area: "components", part: "choices" });
    expect(at(".run-q__title")).toEqual({ area: "components", part: "question" });
    expect(at("#e")).toEqual({ area: "components", part: "messages" });
    expect(at("#b")).toEqual({ area: "components", part: "buttons" });
    expect(at(".run-org")).toEqual({ area: "header" });
    expect(at("#m")).toEqual({ area: "shape" });
    expect(at(".run")).toEqual({ area: "background" });
  });

  it("edits the buttons too, from a click on them or on their label (W46 A: Try moves through the survey)", () => {
    document.body.innerHTML = `<div class="run"><main class="run-main"><div class="run-nav"><button class="run-button" id="next">Next</button></div></main></div>`;
    const picks: unknown[] = [];
    let moved = 0;
    const next = document.getElementById("next")!;
    next.addEventListener("click", () => moved++);
    const stop = startPicking({ "components/buttons": "Buttons · click to edit" }, (pick) => picks.push(pick));

    next.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
    const label = document.querySelector<HTMLElement>(".run-look-label")!;
    label.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
    expect(document.querySelector(".run-look-label")).toBe(label);
    label.click();
    next.click();

    expect(picks).toEqual([{ area: "components", part: "buttons" }, { area: "components", part: "buttons" }]);
    expect(moved).toBe(0);
    stop();
    next.click();
    expect(moved).toBe(1);
  });

  it("sends a click to the editor instead of answering, and stops when asked", () => {
    document.body.innerHTML = `<div class="run"><main class="run-main"><div class="run-choices"><label class="run-choice"><input type="radio" id="r" /></label></div></main></div>`;
    const picks: unknown[] = [];
    const stop = startPicking({ "components/choices": "Choices · click to edit" }, (pick) => picks.push(pick));
    const radio = document.getElementById("r") as HTMLInputElement;

    radio.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
    expect(document.querySelector(".run-look-label")?.textContent).toBe("Choices · click to edit");
    radio.click();
    expect(picks).toEqual([{ area: "components", part: "choices" }]);
    expect(radio.checked).toBe(false);

    stop();
    radio.click();
    expect(radio.checked).toBe(true);
    expect(document.querySelector(".run-look-label")).toBeNull();
  });
});
