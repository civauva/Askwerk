// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type DevelopersClient, type TrialKey } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { curlOf, DevelopersPage, operationsOf } from "./DevelopersPage";

afterEach(cleanup);

const openApi = {
  paths: {
    "/api/v1/surveys": { get: { tags: ["PublicSurveys"], summary: "Lists surveys, newest change first.", description: "Scope surveys:read.", parameters: [{ name: "status", in: "query" }, { name: "limit", in: "query" }] } },
    "/api/v1/surveys/{surveyId}/responses": {
      get: { tags: ["PublicResponses"], summary: "Lists the survey's responses.", description: "Scope responses:read.", parameters: [{ name: "surveyId", in: "path", required: true }, { name: "since", in: "query" }] },
    },
    "/api/v1/webhooks": { post: { tags: ["PublicWebhooks"], summary: "Creates a webhook.", description: "Scope webhooks:manage.", requestBody: {} } },
  },
};

const trial: TrialKey = { key: { id: "k1", scopes: ["surveys:read", "responses:read"], expiresAt: "2026-10-15T18:42:00Z", shown: "aw_…3f9c" }, secret: "aw_trial_secret" };

function fakeClient(trialAnswer: Promise<TrialKey> = Promise.resolve(trial)): DevelopersClient {
  return {
    info: () => Promise.resolve({ baseUrl: "https://surveys.acme.test", apiUrl: "https://surveys.acme.test/api/v1" }),
    trialKey: () => trialAnswer,
    openApi: () => Promise.resolve(openApi),
    postmanUrl: "/api/app/developers/postman",
    openApiUrl: "/api/v1/openapi.json",
    exampleUrl: "/api/app/developers/examples/sql-server-sync",
  };
}

async function shown({ client = fakeClient(), canManageKeys = true, fetcher = vi.fn() as unknown as typeof fetch } = {}) {
  const onOpenApiKeys = vi.fn();
  const view = render(<DevelopersPage client={client} locale="en" canManageKeys={canManageKeys} onOpenApiKeys={onOpenApiKeys} fetcher={fetcher} writeClipboard={() => Promise.resolve()} />);
  await screen.findByRole("heading", { level: 1, name: "Askwerk for developers" });

  return { onOpenApiKeys, ...view };
}

const side = () => within(screen.getByRole("navigation", { name: "Developers hub" }));

describe("Developers hub (AWK-34, W27)", () => {
  it("starts with this Askwerk's API address, the Postman collection and the guides", async () => {
    const { container } = await shown();

    expect(screen.getByText("https://surveys.acme.test/api/v1")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Download Postman collection" }).getAttribute("href")).toBe("/api/app/developers/postman");
    expect(screen.getByText(/curl -H "Authorization: Bearer \$KEY"/).textContent).toContain("https://surveys.acme.test/api/v1/surveys");
    expect(side().getAllByRole("link").map((link) => link.textContent)).toEqual(expect.arrayContaining(["Read responses into your system", "Surveys", "Responses", "Webhooks", "SDKs, samples & downloads"]));
    await expectNoAxeViolations(container);
  });

  it("shows a guide from docs/ with this Askwerk's address", async () => {
    await shown();

    fireEvent.click(side().getByRole("link", { name: "Read responses into your system" }));

    expect(screen.getByRole("heading", { level: 1, name: "Read responses into your system" })).toBeTruthy();
    expect(document.querySelector(".dev-doc")!.textContent).toContain("https://surveys.acme.test");
    expect(document.querySelector(".dev-doc")!.textContent).not.toContain("surveys.example.org");
  });

  it("tries the API with a trial key: the call goes to this Askwerk with the key, and the answer shows", async () => {
    const fetcher = vi.fn(() => Promise.resolve(new Response(JSON.stringify({ items: [{ title: "Pulse" }] }), { status: 200 })));
    const { container } = await shown({ fetcher: fetcher as unknown as typeof fetch });

    fireEvent.click(screen.getByRole("button", { name: "Create a trial key" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Surveys" })).toBeTruthy();
    expect((screen.getByLabelText("Bearer key") as HTMLInputElement).value).toBe("aw_trial_secret");
    fireEvent.change(screen.getByLabelText("status"), { target: { value: "published" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    await waitFor(() => expect(within(screen.getByRole("region", { name: "Response" })).getByText("200")).toBeTruthy());
    expect(fetcher).toHaveBeenCalledWith("/api/v1/surveys?status=published", expect.objectContaining({ method: "GET", headers: { Authorization: "Bearer aw_trial_secret" }, credentials: "omit" }));
    expect(within(screen.getByRole("region", { name: "Response" })).getByText(/"title": "Pulse"/)).toBeTruthy();
    await expectNoAxeViolations(container);
  });

  it("asks before a change goes to real data", async () => {
    const fetcher = vi.fn(() => Promise.resolve(new Response("{}", { status: 201 })));
    await shown({ fetcher: fetcher as unknown as typeof fetch });

    fireEvent.click(side().getByRole("link", { name: "Webhooks" }));
    fireEvent.change(screen.getByLabelText("Bearer key"), { target: { value: "aw_live" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Change real data?" })).getByRole("button", { name: "Cancel" }));
    expect(fetcher).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Change real data?" })).getByRole("button", { name: "Send" }));

    await waitFor(() => expect(fetcher).toHaveBeenCalledWith("/api/v1/webhooks", expect.objectContaining({ method: "POST", body: "{\n}" })));
  });

  it("lets members read but not create keys, and says so when a trial key is refused", async () => {
    await shown({ canManageKeys: false });

    expect(screen.queryByRole("button", { name: "Create a trial key" })).toBeNull();
    expect(side().queryByRole("link", { name: "Your API keys →" })).toBeNull();
    expect(screen.getByText("Owners and admins create keys; ask one of them.")).toBeTruthy();
    cleanup();

    const refused = Promise.reject(new ApiError(403, "4bf92f35", "role-not-allowed"));
    refused.catch(() => undefined);
    await shown({ client: fakeClient(refused) });
    fireEvent.click(screen.getByRole("button", { name: "Create a trial key" }));
    expect(await screen.findByText("Only owners and admins create keys.")).toBeTruthy();
  });

  it("offers the downloads and SDKs", async () => {
    await shown();

    fireEvent.click(side().getByRole("link", { name: "SDKs, samples & downloads" }));

    expect(screen.getByRole("link", { name: "Download collection (.json)" }).getAttribute("href")).toBe("/api/app/developers/postman");
    expect(screen.getByRole("link", { name: "Download (.zip)" }).getAttribute("href")).toBe("/api/app/developers/examples/sql-server-sync");
    expect(screen.getByText("3 requests in 3 folders. baseUrl is set; paste your key into apiKey.")).toBeTruthy();
    expect(screen.getByText("npm install @askwerk/sdk")).toBeTruthy();
  });
});

describe("operationsOf and curlOf", () => {
  it("reads the operations with their area, scope and parameters, and writes curl with this Askwerk's address", () => {
    const [list, responses] = operationsOf(openApi);

    expect([list!.area, list!.scope, responses!.area, responses!.parameters[0]]).toEqual(["Surveys", "surveys:read", "Responses", { name: "surveyId", in: "path", required: true, description: "" }]);
    expect(curlOf(list!, "https://surveys.acme.test/api/v1/surveys", null)).toBe('curl -X GET "https://surveys.acme.test/api/v1/surveys" \\\n  -H "Authorization: Bearer $ASKWERK_KEY"');
  });
});
