// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Askwerk.Api.Identity;

/// <summary>
/// The current security stamp per user, for the session check on every request (AWK-21). A password change,
/// "sign out everywhere" or a two-factor change sets a new stamp, which is remembered here at once, so other sessions
/// end on their next request. Otherwise a stamp is read once and kept for a short time, instead of loading the user
/// and rewriting the cookie on every call. With several app instances, another instance notices within <see cref="KeepFor"/>.
/// </summary>
public sealed class SecurityStamps(IMemoryCache cache)
{
    /// <summary>How long a stamp read from the database is trusted.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(30);

    /// <summary>The user's current stamp; null when the user does not exist or is deactivated.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="context">The database, scoped to that user (row-level security).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stamp.</returns>
    public async Task<string?> CurrentAsync(Guid userId, AskwerkDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (cache.TryGetValue(Key(userId), out string? known))
        {
            return known;
        }

        var stamp = await context.Users
            .Where(user => user.Id == userId && user.DeactivatedAt == null)
            .Select(user => user.SecurityStamp)
            .FirstOrDefaultAsync(cancellationToken);
        cache.Set(Key(userId), stamp, KeepFor);

        return stamp;
    }

    /// <summary>A new stamp was set for the user: sessions with the old one end on their next request.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="stamp">The new stamp.</param>
    public void Remember(Guid userId, string? stamp) => cache.Set(Key(userId), stamp, KeepFor);

    private static string Key(Guid userId) => $"security-stamp:{userId}";
}
