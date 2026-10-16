// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;

namespace Askwerk.Tests.Api;

/// <summary>An authenticator app in a few lines (RFC 6238: HMAC-SHA1, 30-second steps, 6 digits), to sign in as a test user.</summary>
internal static class Totp
{
    public static string Code(string base32Key, DateTimeOffset now)
    {
        var step = now.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

#pragma warning disable CA5350 // Authenticator apps use HMAC-SHA1 (RFC 6238 default); this mirrors them.
        var hash = HMACSHA1.HashData(FromBase32(base32Key), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var bytes = new List<byte>();

        foreach (var character in text.ToUpperInvariant().Where(character => character is not ' ' and not '='))
        {
            value = (value << 5) | alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
