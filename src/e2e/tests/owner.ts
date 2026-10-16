// SPDX-License-Identifier: AGPL-3.0-only
// The owner the first-run flow creates, shared with the flows that sign in afterwards (same e2e run, fresh database).
import { createHmac } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const owner = { name: "Anna Berger", email: "anna@acme.at", password: "correct horse battery" } as const;

const stateFile = join(dirname(fileURLToPath(import.meta.url)), "..", "test-results", "owner-two-factor-key.txt");

export function saveTwoFactorKey(key: string): void {
  mkdirSync(dirname(stateFile), { recursive: true });
  writeFileSync(stateFile, key, "utf8");
}

export function readTwoFactorKey(): string {
  return readFileSync(stateFile, "utf8");
}

/** An authenticator app in a few lines (RFC 6238: HMAC-SHA1, 30-second steps, 6 digits). */
export function totp(base32Key: string, now = Date.now()): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";

  for (const character of base32Key.replace(/[\s=]/g, "").toUpperCase()) {
    bits += alphabet.indexOf(character).toString(2).padStart(5, "0");
  }

  const key = Buffer.from(bits.match(/.{8}/g)!.map((byte) => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(now / 1000 / 30)));
  const hash = createHmac("sha1", key).update(counter).digest();
  const offset = hash[hash.length - 1]! & 0x0f;
  const binary = (hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000;

  return binary.toString().padStart(6, "0");
}
