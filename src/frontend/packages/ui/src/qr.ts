// SPDX-License-Identifier: AGPL-3.0-only
// Our own QR code encoder (ISO/IEC 18004), so no dependency is needed for two-factor setup and survey links.
// Byte mode (UTF-8), error correction level M, versions 1–10 (up to 213 bytes). Follows the standard's steps:
// data bits, Reed–Solomon blocks, function patterns, zig-zag placement, then the mask with the lowest penalty.

const MAX_VERSION = 10;

/** Error-correction codewords per block and number of blocks at level M, for versions 1–10. */
const ECC_PER_BLOCK = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
const BLOCKS = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];

/** Level M in the format information. */
const FORMAT_ECL_M = 0;

/** Encodes text as a QR code; true is a dark module. Throws RangeError when the text is too long. */
export function encodeQr(text: string): boolean[][] {
  const bytes = Array.from(new TextEncoder().encode(text));
  const version = smallestVersion(bytes.length);
  const codewords = addErrorCorrection(dataCodewords(bytes, version), version);
  const size = version * 4 + 17;
  const modules = grid(size, false);
  const isFunction = grid(size, false);

  drawFunctionPatterns(modules, isFunction, version);
  drawCodewords(modules, isFunction, codewords);

  let best: boolean[][] = modules;
  let bestPenalty = Infinity;

  for (let mask = 0; mask < 8; mask++) {
    const candidate = modules.map((row) => [...row]);
    applyMask(candidate, isFunction, mask);
    drawFormatBits(candidate, mask);
    const penalty = penaltyScore(candidate);

    if (penalty < bestPenalty) {
      best = candidate;
      bestPenalty = penalty;
    }
  }

  return best;
}

/** The Reed–Solomon error-correction codewords for the data (generator polynomial over GF(256), 0x11D). */
export function reedSolomonRemainder(data: readonly number[], degree: number): number[] {
  const divisor = reedSolomonDivisor(degree);
  const result = new Array<number>(degree).fill(0);

  for (const byte of data) {
    const factor = byte ^ result.shift()!;
    result.push(0);
    divisor.forEach((coefficient, index) => {
      result[index]! ^= multiply(coefficient, factor);
    });
  }

  return result;
}

function smallestVersion(byteCount: number): number {
  for (let version = 1; version <= MAX_VERSION; version++) {
    const capacityBits = numDataCodewords(version) * 8;
    const neededBits = 4 + characterCountBits(version) + byteCount * 8;

    if (neededBits <= capacityBits) {
      return version;
    }
  }

  throw new RangeError(`Text of ${byteCount} bytes does not fit a version ${MAX_VERSION} QR code.`);
}

function characterCountBits(version: number): number {
  return version <= 9 ? 8 : 16;
}

function numRawDataModules(version: number): number {
  let result = (16 * version + 128) * version + 64;

  if (version >= 2) {
    const alignments = Math.floor(version / 7) + 2;
    result -= (25 * alignments - 10) * alignments - 55;

    if (version >= 7) {
      result -= 36;
    }
  }

  return result;
}

function numDataCodewords(version: number): number {
  return Math.floor(numRawDataModules(version) / 8) - ECC_PER_BLOCK[version]! * BLOCKS[version]!;
}

function dataCodewords(bytes: readonly number[], version: number): number[] {
  const bits: number[] = [];
  const append = (value: number, length: number) => {
    for (let index = length - 1; index >= 0; index--) {
      bits.push((value >>> index) & 1);
    }
  };

  append(0b0100, 4);
  append(bytes.length, characterCountBits(version));
  bytes.forEach((byte) => append(byte, 8));

  const capacityBits = numDataCodewords(version) * 8;
  append(0, Math.min(4, capacityBits - bits.length));
  append(0, (8 - (bits.length % 8)) % 8);

  for (let pad = 0xec; bits.length < capacityBits; pad ^= 0xec ^ 0x11) {
    append(pad, 8);
  }

  const result: number[] = [];

  for (let index = 0; index < bits.length; index += 8) {
    result.push(bits.slice(index, index + 8).reduce((byte, bit) => (byte << 1) | bit, 0));
  }

  return result;
}

function addErrorCorrection(data: readonly number[], version: number): number[] {
  const blockCount = BLOCKS[version]!;
  const eccLength = ECC_PER_BLOCK[version]!;
  const rawCodewords = Math.floor(numRawDataModules(version) / 8);
  const shortBlocks = blockCount - (rawCodewords % blockCount);
  const shortBlockLength = Math.floor(rawCodewords / blockCount);
  const blocks: number[][] = [];

  for (let index = 0, offset = 0; index < blockCount; index++) {
    const dataLength = shortBlockLength - eccLength + (index < shortBlocks ? 0 : 1);
    const block = data.slice(offset, offset + dataLength);
    offset += dataLength;
    const ecc = reedSolomonRemainder(block, eccLength);

    // Short blocks get a placeholder so all blocks interleave column by column.
    blocks.push([...block, ...(index < shortBlocks ? [-1] : []), ...ecc]);
  }

  const result: number[] = [];

  for (let column = 0; column < blocks[0]!.length; column++) {
    for (const block of blocks) {
      if (block[column] !== -1) {
        result.push(block[column]!);
      }
    }
  }

  return result;
}

function reedSolomonDivisor(degree: number): number[] {
  const result = new Array<number>(degree).fill(0);
  result[degree - 1] = 1;
  let root = 1;

  for (let step = 0; step < degree; step++) {
    for (let index = 0; index < degree; index++) {
      result[index] = multiply(result[index]!, root);

      if (index + 1 < degree) {
        result[index]! ^= result[index + 1]!;
      }
    }

    root = multiply(root, 0x02);
  }

  return result;
}

function multiply(x: number, y: number): number {
  let result = 0;

  for (let bit = 7; bit >= 0; bit--) {
    result = (result << 1) ^ ((result >>> 7) * 0x11d);
    result ^= ((y >>> bit) & 1) * x;
  }

  return result;
}

function grid(size: number, value: boolean): boolean[][] {
  return Array.from({ length: size }, () => new Array<boolean>(size).fill(value));
}

function drawFunctionPatterns(modules: boolean[][], isFunction: boolean[][], version: number): void {
  const size = modules.length;
  const set = (x: number, y: number, dark: boolean) => {
    modules[y]![x] = dark;
    isFunction[y]![x] = true;
  };

  for (let index = 0; index < size; index++) {
    set(6, index, index % 2 === 0);
    set(index, 6, index % 2 === 0);
  }

  for (const [x, y] of [
    [3, 3],
    [size - 4, 3],
    [3, size - 4],
  ] as const) {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) {
        const distance = Math.max(Math.abs(dx), Math.abs(dy));
        const px = x + dx;
        const py = y + dy;

        if (px >= 0 && px < size && py >= 0 && py < size) {
          set(px, py, distance !== 2 && distance !== 4);
        }
      }
    }
  }

  const positions = alignmentPositions(version);

  positions.forEach((x, i) =>
    positions.forEach((y, j) => {
      const nearFinder = (i === 0 && j === 0) || (i === 0 && j === positions.length - 1) || (i === positions.length - 1 && j === 0);

      if (!nearFinder) {
        for (let dy = -2; dy <= 2; dy++) {
          for (let dx = -2; dx <= 2; dx++) {
            set(x + dx, y + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
          }
        }
      }
    }),
  );

  // Reserve the format areas (written per mask later) and set the dark module.
  drawFormatBits(modules, 0, isFunction);
  set(8, size - 8, true);

  if (version >= 7) {
    let remainder = version;

    for (let index = 0; index < 12; index++) {
      remainder = (remainder << 1) ^ ((remainder >>> 11) * 0x1f25);
    }

    const bits = (version << 12) | remainder;

    for (let index = 0; index < 18; index++) {
      const dark = ((bits >>> index) & 1) !== 0;
      const a = size - 11 + (index % 3);
      const b = Math.floor(index / 3);
      set(a, b, dark);
      set(b, a, dark);
    }
  }
}

function alignmentPositions(version: number): number[] {
  if (version === 1) {
    return [];
  }

  const count = Math.floor(version / 7) + 2;
  const size = version * 4 + 17;
  const step = Math.floor((version * 8 + count * 3 + 5) / (count * 4 - 4)) * 2;
  const result = [6];

  for (let position = size - 7; result.length < count; position -= step) {
    result.splice(1, 0, position);
  }

  return result;
}

function drawFormatBits(modules: boolean[][], mask: number, isFunction?: boolean[][]): void {
  const size = modules.length;
  const data = (FORMAT_ECL_M << 3) | mask;
  let remainder = data;

  for (let index = 0; index < 10; index++) {
    remainder = (remainder << 1) ^ ((remainder >>> 9) * 0x537);
  }

  const bits = ((data << 10) | remainder) ^ 0x5412;
  const set = (x: number, y: number, bit: number) => {
    modules[y]![x] = ((bits >>> bit) & 1) !== 0;

    if (isFunction !== undefined) {
      isFunction[y]![x] = true;
    }
  };

  for (let index = 0; index <= 5; index++) {
    set(8, index, index);
  }

  set(8, 7, 6);
  set(8, 8, 7);
  set(7, 8, 8);

  for (let index = 9; index < 15; index++) {
    set(14 - index, 8, index);
  }

  for (let index = 0; index < 8; index++) {
    set(size - 1 - index, 8, index);
  }

  for (let index = 8; index < 15; index++) {
    set(8, size - 15 + index, index);
  }
}

function drawCodewords(modules: boolean[][], isFunction: boolean[][], codewords: readonly number[]): void {
  const size = modules.length;
  let bit = 0;

  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) {
      right = 5;
    }

    for (let vertical = 0; vertical < size; vertical++) {
      for (let column = 0; column < 2; column++) {
        const x = right - column;
        const upward = ((right + 1) & 2) === 0;
        const y = upward ? size - 1 - vertical : vertical;

        if (!isFunction[y]![x] && bit < codewords.length * 8) {
          modules[y]![x] = ((codewords[bit >>> 3]! >>> (7 - (bit & 7))) & 1) !== 0;
          bit++;
        }
      }
    }
  }
}

function applyMask(modules: boolean[][], isFunction: boolean[][], mask: number): void {
  const condition: ((x: number, y: number) => boolean)[] = [
    (x, y) => (x + y) % 2 === 0,
    (_x, y) => y % 2 === 0,
    (x) => x % 3 === 0,
    (x, y) => (x + y) % 3 === 0,
    (x, y) => (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0,
    (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
    (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
    (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0,
  ];

  modules.forEach((row, y) =>
    row.forEach((dark, x) => {
      if (!isFunction[y]![x] && condition[mask]!(x, y)) {
        row[x] = !dark;
      }
    }),
  );
}

/** The standard's four penalty rules; the mask with the lowest score is used. */
function penaltyScore(modules: boolean[][]): number {
  const size = modules.length;
  const columns = modules.map((_, x) => modules.map((row) => row[x]!));
  let score = 0;

  for (const line of [...modules, ...columns]) {
    let runColor = line[0]!;
    let runLength = 0;

    for (const dark of line) {
      if (dark === runColor) {
        runLength++;
      } else {
        score += runLength >= 5 ? runLength - 2 : 0;
        runColor = dark;
        runLength = 1;
      }
    }

    score += runLength >= 5 ? runLength - 2 : 0;

    const text = line.map((dark) => (dark ? "1" : "0")).join("");
    score += 40 * ((text.match(/(?=10111010000)/g) ?? []).length + (text.match(/(?=00001011101)/g) ?? []).length);
  }

  for (let y = 0; y < size - 1; y++) {
    for (let x = 0; x < size - 1; x++) {
      const color = modules[y]![x];

      if (color === modules[y]![x + 1] && color === modules[y + 1]![x] && color === modules[y + 1]![x + 1]) {
        score += 3;
      }
    }
  }

  const dark = modules.flat().filter(Boolean).length;
  const total = size * size;
  score += (Math.ceil(Math.abs(dark * 20 - total * 10) / total) - 1) * 10;

  return score;
}
