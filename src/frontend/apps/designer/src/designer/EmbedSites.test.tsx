// SPDX-License-Identifier: AGPL-3.0-only
import { createTranslator } from "@askwerk/schema";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { designerMessages } from "./messages";
import { Published } from "./Published";

afterEach(cleanup);

describe("Share → Embed → Only on these sites (AWK-13, W21 3)", () => {
  it("narrows the survey to some of the organisation's websites", async () => {
    const save = vi.fn(() => Promise.resolve());
    const sites = { load: () => Promise.resolve({ origins: [], organisation: ["https://www.acme.at", "https://intranet.acme.at"] }), save };
    const { container } = render(
      <Published
        t={createTranslator(designerMessages, "en")}
        surveyId="s1"
        title="Pulse"
        language="en"
        links={[{ kind: "system", hostname: "surveys.acme.at", isPrimary: true, url: "https://surveys.acme.at/s/kz26b7mq" }]}
        writeClipboard={() => Promise.resolve()}
        saveFile={() => undefined}
        embedSites={sites}
      />,
    );

    fireEvent.click(await screen.findByText("Only on these sites"));
    fireEvent.click(screen.getByRole("checkbox", { name: "https://intranet.acme.at" }));

    await waitFor(() => expect(save).toHaveBeenCalledWith(["https://intranet.acme.at"]));
    await expectNoAxeViolations(container);
  });
});
