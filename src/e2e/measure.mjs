// SPDX-License-Identifier: AGPL-3.0-only
// Measures a running Askwerk (bin/measure.sh starts the production image): API times as the browser sees them,
// and page loads with their transferred bytes. Sets up a fresh instance with an owner and a published survey first.
import { createHmac } from "node:crypto";
import { chromium } from "@playwright/test";

const base = process.argv[2] ?? "http://localhost:18090";
const cookies = new Map();

async function call(method, path, body) {
  const response = await fetch(base + path, {
    method,
    headers: { "Content-Type": "application/json", Cookie: [...cookies].map(([name, value]) => `${name}=${value}`).join("; ") },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  for (const header of response.headers.getSetCookie()) {
    const [pair] = header.split(";");
    const [name, ...value] = pair.split("=");
    cookies.set(name.trim(), value.join("="));
  }

  const text = await response.text();
  const json = (response.headers.get("content-type") ?? "").includes("json");
  return { status: response.status, body: text && json ? JSON.parse(text) : text, headers: response.headers };
}

function totp(key) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const bits = [...key.replace(/=+$/, "")].map((character) => alphabet.indexOf(character).toString(2).padStart(5, "0")).join("");
  const secret = Buffer.from(bits.match(/.{8}/g).map((byte) => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const hash = createHmac("sha1", secret).update(counter).digest();
  const offset = hash[hash.length - 1] & 15;
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1000000).padStart(6, "0");
}

const median = (values) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];

async function time(label, method, path, body, runs = 30) {
  const durations = [];
  let setCookies = 0;

  for (let run = 0; run < runs; run++) {
    const started = performance.now();
    const response = await call(method, path, body);
    durations.push(performance.now() - started);
    setCookies += response.headers.getSetCookie().length > 0 ? 1 : 0;
  }

  return { label, medianMs: Math.round(median(durations) * 10) / 10, p90Ms: Math.round([...durations].sort((a, b) => a - b)[Math.floor(runs * 0.9)] * 10) / 10, cookieRewrites: `${setCookies}/${runs}` };
}

// Setup: owner with two-factor, a published survey.
await call("POST", "/api/app/setup", {
  owner: { name: "Measure Owner", email: "owner@measure.test", password: "correct horse battery" },
  instance: { name: "Measure", baseAddress: base, defaultLanguage: "en", timeZone: "UTC" },
  organisation: { name: "Measure", slug: "measure" },
});
const { body: setup } = await call("GET", "/api/app/account/two-factor");
await call("POST", "/api/app/account/two-factor", { code: totp(setup.sharedKey) });
const { body: created } = await call("POST", "/api/app/surveys", { title: "Measure", languages: ["en"], defaultLanguage: "en" });
await call("PUT", `/api/app/surveys/${created.id}/draft`, {
  definition: {
    schemaVersion: 1,
    languages: ["en"],
    defaultLanguage: "en",
    pages: [{ id: "p1", questions: [{ id: "q1", variable: "SAT", type: "rating", text: { en: "How satisfied?" }, required: true, scale: { min: 1, max: 5 } }] }],
  },
});
await call("POST", `/api/app/surveys/${created.id}/publish`);

const api = [
  await time("GET /api/app/me", "GET", "/api/app/me"),
  await time("GET /api/app/surveys", "GET", "/api/app/surveys"),
  await time("GET /api/app/surveys/{id}/draft", "GET", `/api/app/surveys/${created.id}/draft`),
  await time("GET /api/app/run/{id} (anonymous)", "GET", `/api/app/run/${created.id}`),
  await time("GET /health/ready", "GET", "/health/ready"),
];

// Pages: cold load (empty cache) and warm load (second visit), with what went over the wire.
const browser = await chromium.launch();
const pages = [];

for (const [label, path, signedIn] of [
  ["sign-in page", "/", false],
  ["survey list (signed in)", "/", true],
  ["runner", `/s/${created.id}`, false],
]) {
  const context = await browser.newContext();

  if (signedIn) {
    await context.addCookies([...cookies].map(([name, value]) => ({ name, value, url: base })));
  }

  const page = await context.newPage();
  const loads = [];

  for (const visit of ["cold", "warm"]) {
    await page.goto(base + path, { waitUntil: "networkidle" });
    loads.push(
      await page.evaluate((visit) => {
        const navigation = performance.getEntriesByType("navigation")[0];
        const resources = performance.getEntriesByType("resource");
        const bytes = resources.reduce((sum, entry) => sum + entry.transferSize, navigation.transferSize);
        const fromCache = resources.filter((entry) => entry.transferSize === 0 && entry.decodedBodySize > 0).length;
        return { visit, domContentLoadedMs: Math.round(navigation.domContentLoadedEventEnd), loadMs: Math.round(navigation.loadEventEnd), kilobytes: Math.round(bytes / 1024), fromCache: `${fromCache}/${resources.length}` };
      }, visit),
    );
  }

  pages.push({ label, loads });
  await context.close();
}

await browser.close();
console.log(JSON.stringify({ api, pages }, null, 2));
