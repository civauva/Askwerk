// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { canEditSurveys } from "./roles";
import { anna } from "./testing";

describe("canEditSurveys (AWK-27)", () => {
  it("lets owners, admins and editors edit; analysts and viewers read", () => {
    const as = (role: string) => ({ ...anna, organisations: [{ ...anna.organisations[0]!, role }] });

    expect(["owner", "admin", "editor", "analyst", "viewer"].map((role) => canEditSurveys(as(role)))).toEqual([true, true, true, false, false]);
  });
});
