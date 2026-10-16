// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Application.Domains;

/// <summary>Reads the DNS records that verify a custom domain (AWK-7). A record that cannot be read counts as missing.</summary>
public interface IDnsLookup
{
    /// <summary>The CNAME target of a name.</summary>
    /// <param name="name">The name, e.g. surveys.acme.at.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The target, possibly with a trailing dot; null when there is none.</returns>
    Task<string?> CnameAsync(string name, CancellationToken cancellationToken);

    /// <summary>The TXT records of a name, each read as one text.</summary>
    /// <param name="name">The name, e.g. _askwerk.surveys.acme.at.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The texts; empty when there are none.</returns>
    Task<IReadOnlyList<string>> TxtAsync(string name, CancellationToken cancellationToken);
}
