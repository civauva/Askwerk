// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written DomainsClient for the Domains page's tests.
import { ApiError, type DomainItem, type DomainList, type DomainsClient, type HostLookSettings } from "@askwerk/app-client";

const record = (part: Partial<DomainItem> & Pick<DomainItem, "id" | "hostname" | "status">): DomainItem => ({
  isPrimary: false,
  cnameTarget: "surveys.askwerk.app",
  txtName: `_askwerk.${part.hostname}`,
  txtValue: "askwerk-verify=9f3c00000000000000000000000000a21e",
  cnameFound: false,
  txtFound: false,
  createdAt: "2026-10-14T08:00:00Z",
  checkedAt: null,
  verifiedAt: null,
  ...part,
});

export class FakeDomainsClient implements DomainsClient {
  readonly calls: [string, ...unknown[]][] = [];
  systemPrimary = false;
  items: DomainItem[] = [
    record({ id: "d1", hostname: "surveys.acme.at", status: "verified", isPrimary: true, cnameFound: true, txtFound: true }),
    record({ id: "d2", hostname: "umfragen.acme.at", status: "pending", cnameFound: true, checkedAt: "2026-10-14T09:58:00Z" }),
    record({ id: "d3", hostname: "survey.acme.com", status: "failed", checkedAt: "2026-10-13T09:00:00Z" }),
  ];
  origins = ["https://www.acme.at"];
  /** What DNS shows on the next Verify. */
  nextCheck: Pick<DomainItem, "cnameFound" | "txtFound"> = { cnameFound: true, txtFound: true };

  list = () => {
    this.calls.push(["list"]);
    return Promise.resolve<DomainList>({ system: { hostname: "surveys.askwerk.app", isPrimary: this.systemPrimary }, items: [...this.items] });
  };

  add = (hostname: string) => {
    this.calls.push(["add", hostname]);
    const name = hostname.trim().toLowerCase();

    if (!name.includes(".") || name.includes("/")) {
      return Promise.reject(new ApiError(400, undefined, "bad", { errors: { hostname: ["hostname-invalid"] } }));
    }

    if (this.items.some((item) => item.hostname === name)) {
      return Promise.reject(new ApiError(409, undefined, "taken", { code: "domain-taken" }));
    }

    const added = record({ id: `d${this.items.length + 1}`, hostname: name, status: "pending" });
    this.items = [added, ...this.items];
    return Promise.resolve(added);
  };

  verify = (id: string) => {
    this.calls.push(["verify", id]);
    const found = this.items.find((item) => item.id === id)!;
    const checked: DomainItem = { ...found, ...this.nextCheck, status: this.nextCheck.cnameFound && this.nextCheck.txtFound ? "verified" : "pending", checkedAt: "2026-10-14T10:00:00Z" };
    this.items = this.items.map((item) => (item.id === id ? checked : item));
    return Promise.resolve(checked);
  };

  setPrimary = (id: string | null) => {
    this.calls.push(["setPrimary", id]);
    this.items = this.items.map((item) => ({ ...item, isPrimary: item.id === id }));
    this.systemPrimary = id === null;
    return Promise.resolve();
  };

  remove = (id: string) => {
    this.calls.push(["remove", id]);
    this.items = this.items.filter((item) => item.id !== id);
    this.systemPrimary ||= !this.items.some((item) => item.isPrimary);
    return Promise.resolve();
  };

  embedding = () => {
    this.calls.push(["embedding"]);
    return Promise.resolve([...this.origins]);
  };

  designerOrigins: string[] = [];
  survey: { origins: string[]; organisation: string[] } = { origins: [], organisation: [] };

  designerEmbedding = () => Promise.resolve([...this.designerOrigins]);
  look: HostLookSettings | null = null;
  hostLook = () => Promise.resolve(this.look);
  setHostLook = (look: HostLookSettings) => {
    this.calls.push(["setHostLook", look]);
    this.look = look;
    return Promise.resolve(look);
  };

  setDesignerEmbedding = (origins: readonly string[]) => {
    this.calls.push(["setDesignerEmbedding", [...origins]]);
    this.designerOrigins = [...origins];
    return Promise.resolve();
  };

  surveyEmbedding = (_surveyId: string) => Promise.resolve({ origins: [...this.survey.origins], organisation: [...this.survey.organisation] });

  setSurveyEmbedding = (surveyId: string, origins: readonly string[]) => {
    this.calls.push(["setSurveyEmbedding", surveyId, [...origins]]);
    this.survey = { ...this.survey, origins: [...origins] };
    return Promise.resolve();
  };

  setEmbedding = (origins: readonly string[]) => {
    this.calls.push(["setEmbedding", [...origins]]);

    if (origins.some((origin) => !/^https?:\/\//.test(origin))) {
      return Promise.reject(new ApiError(400, undefined, "bad", { errors: { origins: ["origin-invalid"] } }));
    }

    this.origins = [...origins];
    return Promise.resolve();
  };
}
