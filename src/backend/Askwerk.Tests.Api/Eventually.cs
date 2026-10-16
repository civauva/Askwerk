// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Tests.Api;

/// <summary>Polls a condition for at most two seconds, for effects that happen just after a response.</summary>
internal static class Eventually
{
    public static async Task<bool> TrueAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}
