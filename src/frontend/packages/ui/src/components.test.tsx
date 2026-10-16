// SPDX-License-Identifier: AGPL-3.0-only
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Button, QrCode, Skeleton, StatusPill } from "./index";
import { expectNoAxeViolations } from "./testing";

afterEach(cleanup);

describe("StatusPill", () => {
  it("shows its state as text, not only as a colour or dot", () => {
    render(<StatusPill tone="positive">Database ready</StatusPill>);

    expect(screen.getByText("Database ready")).toBeTruthy();
  });

  it("has no accessibility violations", async () => {
    const { container } = render(<StatusPill tone="negative">Database not ready</StatusPill>);

    await expectNoAxeViolations(container);
  });
});

describe("Button", () => {
  it("is a real button that calls onClick", () => {
    let clicks = 0;
    render(<Button onClick={() => clicks++}>Try again</Button>);

    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(clicks).toBe(1);
  });
});

describe("Skeleton", () => {
  it("is hidden from assistive technology", () => {
    const { container } = render(<Skeleton width="12rem" height="1.25rem" />);

    expect(container.firstElementChild?.getAttribute("aria-hidden")).toBe("true");
  });
});

describe("QrCode", () => {
  it("is one image with a name and draws its dark modules as one path", () => {
    render(<QrCode value="otpauth://totp/Askwerk:anna%40acme.at?secret=JBSWY3DPEHPK3PXP" label="QR code for your authenticator app" />);

    const image = screen.getByRole("img", { name: "QR code for your authenticator app" });

    expect(image.querySelectorAll("path")).toHaveLength(1);
    expect(image.getAttribute("viewBox")).toBe("0 0 41 41");
  });
});
