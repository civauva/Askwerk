// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { encodeQr, reedSolomonRemainder } from "./qr";

/** Reads the 15 format bits next to the top-left finder pattern, most significant first (ISO/IEC 18004, 7.9). */
function formatBits(modules: boolean[][]): number {
  const positions: [number, number][] = [
    [0, 8], [1, 8], [2, 8], [3, 8], [4, 8], [5, 8], [7, 8],
    [8, 8], [8, 7], [8, 5], [8, 4], [8, 3], [8, 2], [8, 1], [8, 0],
  ];
  let bits = 0;

  for (const [x, y] of positions) {
    bits = (bits << 1) | (modules[y]![x]! ? 1 : 0);
  }

  return bits;
}

const isFinderAt = (modules: boolean[][], left: number, top: number) =>
  [0, 1, 2, 3, 4, 5, 6].every((dy) =>
    [0, 1, 2, 3, 4, 5, 6].every((dx) => {
      const ring = Math.max(Math.abs(dx - 3), Math.abs(dy - 3));
      return modules[top + dy]![left + dx] === (ring !== 2);
    }),
  );

describe("reedSolomonRemainder", () => {
  it("gives the error-correction codewords of the standard's 1-M example", () => {
    const data = [0x10, 0x20, 0x0c, 0x56, 0x61, 0x80, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11];

    const ecc = reedSolomonRemainder(data, 10);

    expect(ecc).toEqual([0xa5, 0x24, 0xd4, 0xc1, 0xed, 0x36, 0xc7, 0x87, 0x2c, 0x55]);
  });
});

describe("encodeQr", () => {
  it("draws the three finder patterns in the corners", () => {
    const modules = encodeQr("https://acme.askwerk.app/s/kz26");
    const size = modules.length;

    expect([isFinderAt(modules, 0, 0), isFinderAt(modules, size - 7, 0), isFinderAt(modules, 0, size - 7)]).toEqual([true, true, true]);
  });

  it("alternates the timing pattern between the finders", () => {
    const modules = encodeQr("https://acme.askwerk.app/s/kz26");

    const row = modules[6]!.slice(8, modules.length - 8);

    expect(row.every((dark, index) => dark === (index % 2 === 0))).toBe(true);
  });

  it("writes valid format information for error correction level M", () => {
    const modules = encodeQr("otpauth://totp/Acme%20Surveys:anna%40acme.at?secret=JBSWY3DPEHPK3PXP&issuer=Acme%20Surveys&digits=6");

    const data = (formatBits(modules) ^ 0x5412) >> 10;

    expect(data >> 3).toBe(0);
    expect(reedSolomonFormatCheck(formatBits(modules) ^ 0x5412)).toBe(0);
  });

  it.each([
    ["a", 21],
    ["x".repeat(106), 41],
    ["x".repeat(107), 45],
  ])("picks the smallest version that fits %#", (text, size) => {
    const modules = encodeQr(text);

    expect(modules.length).toBe(size);
  });

  it("refuses text longer than version 10 holds", () => {
    expect(() => encodeQr("x".repeat(214))).toThrow(RangeError);
  });
});

/** The BCH(15,5) remainder of format bits; zero when the bits are valid. */
function reedSolomonFormatCheck(bits: number): number {
  let remainder = bits;

  for (let shift = 14; shift >= 10; shift--) {
    if ((remainder >> shift) & 1) {
      remainder ^= 0x537 << (shift - 10);
    }
  }

  return remainder;
}

describe("encodeQr (golden corpus shared with the server's QrCode, AWK-17)", () => {
  const corpus = Object.values(import.meta.glob<{ text: string; modules: string[] }>("../tests/corpus/qr/*.json", { eager: true, import: "default" }));

  it.each(corpus.map((c) => [c.text, c] as const))("%s", (_text, c) => {
    expect(encodeQr(c.text).map((row) => row.map((dark) => (dark ? "1" : "0")).join(""))).toEqual(c.modules);
  });
});
