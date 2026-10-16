// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;

namespace Askwerk.Domain.Surveys;

/// <summary>
/// Short codes for survey links (AWK-7): /s/kz26b7mq instead of the long id. Eight characters from an alphabet without
/// look-alikes (no 0/o, 1/l/i), so a code read off paper or a poster is typed right; 31^8 codes keep collisions rare,
/// and a unique index refuses the rest.
/// </summary>
public static class ShortCode
{
    /// <summary>The characters codes are made of.</summary>
    public const string Alphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    /// <summary>Characters in a code.</summary>
    public const int Length = 8;

    /// <summary>A new random code.</summary>
    /// <returns>Eight characters from <see cref="Alphabet"/>.</returns>
    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);
}
