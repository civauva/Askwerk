// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type UnsubscribeInfo } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Unsubscribe } from "./Unsubscribe";

afterEach(cleanup);

const info: UnsubscribeInfo = { organisation: "Acme GmbH", email: "a***@acme.at", language: null, done: false };

describe("Unsubscribe (AWK-7): one click, no account", () => {
  it("asks once, and confirms that no more invitations come", async () => {
    const confirm = vi.fn(() => Promise.resolve());
    const { container } = render(<Unsubscribe load={() => Promise.resolve(info)} confirm={confirm} initialLocale="en" />);

    expect((await screen.findByRole("heading", { level: 1 })).textContent).toBe("No more invitations from Acme GmbH");
    expect(screen.getByText("Acme GmbH will not send survey invitations to a***@acme.at any more.")).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "Unsubscribe" }));

    expect((await screen.findByRole("status")).textContent).toBe("Done. Acme GmbH will not invite a***@acme.at again.");
    expect(confirm).toHaveBeenCalledTimes(1);
  });

  it("speaks the person's language, and says when it is already done", async () => {
    render(<Unsubscribe load={() => Promise.resolve({ ...info, language: "de", done: true })} confirm={vi.fn()} initialLocale="en" />);

    expect((await screen.findByText("Erledigt. Acme GmbH lädt a***@acme.at nicht mehr ein.")).getAttribute("role")).toBe("status");
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("says when the link is not valid", async () => {
    render(<Unsubscribe load={() => Promise.reject(new ApiError(404, undefined, "not found"))} confirm={vi.fn()} initialLocale="en" />);

    expect((await screen.findByRole("heading", { level: 1 })).textContent).toBe("This link is not valid");
  });
});
