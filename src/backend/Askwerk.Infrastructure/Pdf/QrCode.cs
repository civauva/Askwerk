// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using SkiaSharp;

namespace Askwerk.Infrastructure.Pdf;

/// <summary>
/// Our own QR code encoder (ISO/IEC 18004), the twin of packages/ui/src/qr.ts; a shared golden corpus keeps both
/// drawing the same modules. Byte mode (UTF-8), error correction level M, versions 1–10 (up to 213 bytes).
/// </summary>
public static class QrCode
{
    private const int MaxVersion = 10;

    /// <summary>Error-correction codewords per block at level M, for versions 1–10.</summary>
    private static readonly int[] EccPerBlock = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];

    /// <summary>Number of blocks at level M, for versions 1–10.</summary>
    private static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];

    private static readonly Func<int, int, bool>[] Masks =
    [
        (x, y) => (x + y) % 2 == 0,
        (_, y) => y % 2 == 0,
        (x, _) => x % 3 == 0,
        (x, y) => (x + y) % 3 == 0,
        (x, y) => ((x / 3) + (y / 2)) % 2 == 0,
        (x, y) => ((x * y) % 2) + ((x * y) % 3) == 0,
        (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
        (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
    ];

    /// <summary>Encodes text as a QR code; true is a dark module.</summary>
    /// <param name="text">The text, e.g. a survey link.</param>
    /// <returns>Rows of modules.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The text does not fit a version 10 code.</exception>
    public static bool[][] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        var version = SmallestVersion(bytes.Length);
        var codewords = AddErrorCorrection(DataCodewords(bytes, version), version);
        var size = (version * 4) + 17;
        var modules = Grid(size);
        var isFunction = Grid(size);

        DrawFunctionPatterns(modules, isFunction, version);
        DrawCodewords(modules, isFunction, codewords);

        var best = modules;
        var bestPenalty = int.MaxValue;

        for (var mask = 0; mask < 8; mask++)
        {
            var candidate = modules.Select(row => (bool[])row.Clone()).ToArray();
            ApplyMask(candidate, isFunction, mask);
            DrawFormatBits(candidate, mask, null);
            var penalty = PenaltyScore(candidate);

            if (penalty < bestPenalty)
            {
                best = candidate;
                bestPenalty = penalty;
            }
        }

        return best;
    }

    /// <summary>The code as a black and white PNG with the standard quiet zone of four modules, for printing.</summary>
    /// <param name="text">The text, e.g. a survey link.</param>
    /// <param name="moduleSize">Pixels per module.</param>
    /// <returns>PNG bytes.</returns>
    public static byte[] Png(string text, int moduleSize = 8)
    {
        var modules = Encode(text);
        var size = (modules.Length + 8) * moduleSize;
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        using (var dark = new SKPaint { Color = SKColors.Black, IsAntialias = false })
        {
            canvas.Clear(SKColors.White);

            for (var y = 0; y < modules.Length; y++)
            {
                for (var x = 0; x < modules.Length; x++)
                {
                    if (modules[y][x])
                    {
                        canvas.DrawRect((x + 4) * moduleSize, (y + 4) * moduleSize, moduleSize, moduleSize, dark);
                    }
                }
            }
        }

        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        return png.ToArray();
    }

    private static int SmallestVersion(int byteCount)
    {
        for (var version = 1; version <= MaxVersion; version++)
        {
            if (4 + CharacterCountBits(version) + (byteCount * 8) <= NumDataCodewords(version) * 8)
            {
                return version;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(byteCount), $"Text of {byteCount} bytes does not fit a version {MaxVersion} QR code.");
    }

    private static int CharacterCountBits(int version) => version <= 9 ? 8 : 16;

    private static int NumRawDataModules(int version)
    {
        var result = (((16 * version) + 128) * version) + 64;

        if (version >= 2)
        {
            var alignments = (version / 7) + 2;
            result -= (((25 * alignments) - 10) * alignments) - 55;

            if (version >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    private static int NumDataCodewords(int version) => (NumRawDataModules(version) / 8) - (EccPerBlock[version] * Blocks[version]);

    private static List<int> DataCodewords(byte[] bytes, int version)
    {
        var bits = new List<int>();

        void Append(int value, int length)
        {
            for (var index = length - 1; index >= 0; index--)
            {
                bits.Add((value >> index) & 1);
            }
        }

        Append(0b0100, 4);
        Append(bytes.Length, CharacterCountBits(version));

        foreach (var value in bytes)
        {
            Append(value, 8);
        }

        var capacityBits = NumDataCodewords(version) * 8;
        Append(0, Math.Min(4, capacityBits - bits.Count));
        Append(0, (8 - (bits.Count % 8)) % 8);

        for (var pad = 0xec; bits.Count < capacityBits; pad ^= 0xec ^ 0x11)
        {
            Append(pad, 8);
        }

        var result = new List<int>();

        for (var index = 0; index < bits.Count; index += 8)
        {
            result.Add(bits.Skip(index).Take(8).Aggregate(0, (value, bit) => (value << 1) | bit));
        }

        return result;
    }

    private static List<int> AddErrorCorrection(List<int> data, int version)
    {
        var blockCount = Blocks[version];
        var eccLength = EccPerBlock[version];
        var rawCodewords = NumRawDataModules(version) / 8;
        var shortBlocks = blockCount - (rawCodewords % blockCount);
        var shortBlockLength = rawCodewords / blockCount;
        var blocks = new List<List<int>>();

        for (int index = 0, offset = 0; index < blockCount; index++)
        {
            var dataLength = shortBlockLength - eccLength + (index < shortBlocks ? 0 : 1);
            var block = data.GetRange(offset, dataLength);
            offset += dataLength;
            var ecc = ReedSolomonRemainder(block, eccLength);

            // Short blocks get a placeholder so all blocks interleave column by column.
            blocks.Add([.. block, .. index < shortBlocks ? [-1] : Array.Empty<int>(), .. ecc]);
        }

        var result = new List<int>();

        for (var column = 0; column < blocks[0].Count; column++)
        {
            foreach (var block in blocks.Where(block => block[column] != -1))
            {
                result.Add(block[column]);
            }
        }

        return result;
    }

    private static List<int> ReedSolomonRemainder(List<int> data, int degree)
    {
        var divisor = ReedSolomonDivisor(degree);
        var result = Enumerable.Repeat(0, degree).ToList();

        foreach (var value in data)
        {
            var factor = value ^ result[0];
            result.RemoveAt(0);
            result.Add(0);

            for (var index = 0; index < divisor.Length; index++)
            {
                result[index] ^= Multiply(divisor[index], factor);
            }
        }

        return result;
    }

    private static int[] ReedSolomonDivisor(int degree)
    {
        var result = new int[degree];
        result[degree - 1] = 1;
        var root = 1;

        for (var step = 0; step < degree; step++)
        {
            for (var index = 0; index < degree; index++)
            {
                result[index] = Multiply(result[index], root);

                if (index + 1 < degree)
                {
                    result[index] ^= result[index + 1];
                }
            }

            root = Multiply(root, 0x02);
        }

        return result;
    }

    private static int Multiply(int x, int y)
    {
        var result = 0;

        for (var bit = 7; bit >= 0; bit--)
        {
            result = (result << 1) ^ ((result >> 7) * 0x11d);
            result ^= ((y >> bit) & 1) * x;
        }

        return result;
    }

    private static bool[][] Grid(int size) => [.. Enumerable.Range(0, size).Select(_ => new bool[size])];

    private static void DrawFunctionPatterns(bool[][] modules, bool[][] isFunction, int version)
    {
        var size = modules.Length;

        void Set(int x, int y, bool dark)
        {
            modules[y][x] = dark;
            isFunction[y][x] = true;
        }

        for (var index = 0; index < size; index++)
        {
            Set(6, index, index % 2 == 0);
            Set(index, 6, index % 2 == 0);
        }

        foreach (var (x, y) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    var px = x + dx;
                    var py = y + dy;

                    if (px >= 0 && px < size && py >= 0 && py < size)
                    {
                        Set(px, py, distance != 2 && distance != 4);
                    }
                }
            }
        }

        var positions = AlignmentPositions(version);

        for (var i = 0; i < positions.Count; i++)
        {
            for (var j = 0; j < positions.Count; j++)
            {
                var nearFinder = (i == 0 && j == 0) || (i == 0 && j == positions.Count - 1) || (i == positions.Count - 1 && j == 0);

                if (nearFinder)
                {
                    continue;
                }

                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        Set(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                }
            }
        }

        // Reserve the format areas (written per mask later) and set the dark module.
        DrawFormatBits(modules, 0, isFunction);
        Set(8, size - 8, true);

        if (version >= 7)
        {
            var remainder = version;

            for (var index = 0; index < 12; index++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1f25);
            }

            var bits = (version << 12) | remainder;

            for (var index = 0; index < 18; index++)
            {
                var dark = ((bits >> index) & 1) != 0;
                var a = size - 11 + (index % 3);
                var b = index / 3;
                Set(a, b, dark);
                Set(b, a, dark);
            }
        }
    }

    private static List<int> AlignmentPositions(int version)
    {
        if (version == 1)
        {
            return [];
        }

        var count = (version / 7) + 2;
        var size = (version * 4) + 17;
        var step = (((version * 8) + (count * 3) + 5) / ((count * 4) - 4)) * 2;
        var result = new List<int> { 6 };

        for (var position = size - 7; result.Count < count; position -= step)
        {
            result.Insert(1, position);
        }

        return result;
    }

    private static void DrawFormatBits(bool[][] modules, int mask, bool[][]? isFunction)
    {
        var size = modules.Length;
        const int levelM = 0;
        var data = (levelM << 3) | mask;
        var remainder = data;

        for (var index = 0; index < 10; index++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        var bits = ((data << 10) | remainder) ^ 0x5412;

        void Set(int x, int y, int bit)
        {
            modules[y][x] = ((bits >> bit) & 1) != 0;

            if (isFunction is not null)
            {
                isFunction[y][x] = true;
            }
        }

        for (var index = 0; index <= 5; index++)
        {
            Set(8, index, index);
        }

        Set(8, 7, 6);
        Set(8, 8, 7);
        Set(7, 8, 8);

        for (var index = 9; index < 15; index++)
        {
            Set(14 - index, 8, index);
        }

        for (var index = 0; index < 8; index++)
        {
            Set(size - 1 - index, 8, index);
        }

        for (var index = 8; index < 15; index++)
        {
            Set(8, size - 15 + index, index);
        }
    }

    private static void DrawCodewords(bool[][] modules, bool[][] isFunction, List<int> codewords)
    {
        var size = modules.Length;
        var bit = 0;

        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (var vertical = 0; vertical < size; vertical++)
            {
                for (var column = 0; column < 2; column++)
                {
                    var x = right - column;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vertical : vertical;

                    if (!isFunction[y][x] && bit < codewords.Count * 8)
                    {
                        modules[y][x] = ((codewords[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                        bit++;
                    }
                }
            }
        }
    }

    private static void ApplyMask(bool[][] modules, bool[][] isFunction, int mask)
    {
        for (var y = 0; y < modules.Length; y++)
        {
            for (var x = 0; x < modules.Length; x++)
            {
                if (!isFunction[y][x] && Masks[mask](x, y))
                {
                    modules[y][x] = !modules[y][x];
                }
            }
        }
    }

    /// <summary>The standard's four penalty rules; the mask with the lowest score is used.</summary>
    private static int PenaltyScore(bool[][] modules)
    {
        var size = modules.Length;
        var lines = modules.Concat(Enumerable.Range(0, size).Select(x => modules.Select(row => row[x]).ToArray()));
        var score = 0;

        foreach (var line in lines)
        {
            var runColour = line[0];
            var runLength = 0;

            foreach (var dark in line)
            {
                if (dark == runColour)
                {
                    runLength++;
                }
                else
                {
                    score += runLength >= 5 ? runLength - 2 : 0;
                    runColour = dark;
                    runLength = 1;
                }
            }

            score += runLength >= 5 ? runLength - 2 : 0;
            var text = string.Concat(line.Select(dark => dark ? '1' : '0'));
            score += 40 * (Count(text, "10111010000") + Count(text, "00001011101"));
        }

        for (var y = 0; y < size - 1; y++)
        {
            for (var x = 0; x < size - 1; x++)
            {
                var colour = modules[y][x];

                if (colour == modules[y][x + 1] && colour == modules[y + 1][x] && colour == modules[y + 1][x + 1])
                {
                    score += 3;
                }
            }
        }

        var darkCount = modules.Sum(row => row.Count(dark => dark));
        var total = size * size;
        score += ((int)Math.Ceiling(Math.Abs((darkCount * 20.0) - (total * 10.0)) / total) - 1) * 10;

        return score;
    }

    /// <summary>Overlapping occurrences, as the TypeScript twin counts them with a lookahead.</summary>
    private static int Count(string text, string pattern)
    {
        var count = 0;

        for (var index = text.IndexOf(pattern, StringComparison.Ordinal); index >= 0; index = text.IndexOf(pattern, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
