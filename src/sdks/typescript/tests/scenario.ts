// SPDX-License-Identifier: Apache-2.0
// The SDK scenario against a running Askwerk (run by src/backend/Askwerk.Tests.Api/SdkTests.cs): node tests/scenario.ts
import assert from "node:assert/strict";
import { Askwerk, AskwerkError, verifyWebhook } from "../src/index.ts";

const env = (name: string) => process.env[name] ?? assert.fail(`${name} is not set`);
const baseUrl = env("ASKWERK_URL");
const survey = env("ASKWERK_SURVEY");
const askwerk = new Askwerk({ baseUrl, apiKey: env("ASKWERK_KEY") });

const published: string[] = [];
for await (const item of askwerk.surveys.listAll({ status: "published" })) published.push(item.id);
assert.deepEqual(published, [survey]);
assert.equal((await askwerk.surveys.get(survey)).title, "Customer satisfaction 2026");
assert.ok((await askwerk.responses.codebook(survey, { language: "de" })).variables.some((variable) => variable.variable === "BEFORE"));

const responses = [];
for await (const response of askwerk.responses.listAll(survey)) responses.push(response);
assert.equal(responses.length, 1);
assert.ok(responses[0]!.answers.some((answer) => answer.variable === "SAT" && answer.value === "4"));

const started = await askwerk.exports.start(survey, { format: "csv" });
const ready = await askwerk.exports.waitUntilReady(started.id, { intervalMs: 200, timeoutMs: 60_000 });
assert.ok((await askwerk.exports.download(ready.id)).length > 0);

const created = await askwerk.webhooks.create({ url: "https://hooks.example.org/askwerk", events: ["survey.published"] });
assert.ok((await askwerk.webhooks.list()).items.some((each) => each.id === created.webhook.id));
await askwerk.webhooks.delete(created.webhook.id);

await assert.rejects(askwerk.surveys.get(crypto.randomUUID()), (error) => error instanceof AskwerkError && error.status === 404 && error.type === "https://askwerk.org/problems/survey-not-found");

const client = new Askwerk({ baseUrl, clientId: env("ASKWERK_CLIENT_ID"), clientSecret: env("ASKWERK_CLIENT_SECRET") });
assert.equal((await client.surveys.list()).items.length, 1);

assert.ok(await verifyWebhook("whsec_test", env("ASKWERK_SIGNATURE"), env("ASKWERK_SIGNED_BODY")));
assert.ok(!(await verifyWebhook("whsec_test", env("ASKWERK_SIGNATURE"), env("ASKWERK_SIGNED_BODY") + " ")));
console.log("TypeScript SDK: ok");
