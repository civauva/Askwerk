// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Setup;
using Askwerk.Domain.Domains;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Surveys;

/// <summary>
/// Where a survey is answered (AWK-7, W12 B): /s/{code} on the system address and on every verified domain of its
/// organisation, the primary one first. Links, QR codes, the PDF and invitations take the primary one.
/// </summary>
internal static class SurveyLinks
{
    /// <summary>The survey's links, primary first.</summary>
    public static async Task<IReadOnlyList<SurveyLinkResponse>> OfAsync(AskwerkDbContext context, IInstanceSettingsRepository settings, HttpRequest request, Survey survey, CancellationToken cancellationToken) =>
        await OfAsync(context, (await settings.FindAsync(cancellationToken))?.BaseAddress ?? $"{request.Scheme}://{request.Host}", survey, cancellationToken);

    /// <summary>The survey's links, primary first, from the instance's address (background jobs have no request).</summary>
    public static async Task<IReadOnlyList<SurveyLinkResponse>> OfAsync(AskwerkDbContext context, string instanceAddress, Survey survey, CancellationToken cancellationToken)
    {
        var baseAddress = instanceAddress.TrimEnd('/');
        var system = new Uri(baseAddress);
        var domains = await context.Domains
            .Where(domain => domain.TenantId == survey.TenantId && domain.Status == DomainStatus.Verified)
            .OrderBy(domain => domain.Hostname)
            .ToListAsync(cancellationToken);
        var links = new List<SurveyLinkResponse>
        {
            new("system", system.Host, !domains.Any(domain => domain.IsPrimary), $"{baseAddress}/s/{survey.Code}"),
        };
        links.AddRange(domains.Select(domain => new SurveyLinkResponse("domain", domain.Hostname, domain.IsPrimary, $"{system.Scheme}://{domain.Hostname}/s/{survey.Code}")));

        return [.. links.OrderByDescending(link => link.IsPrimary)];
    }

    /// <summary>The primary link, or the one on the host asked for when the survey has it.</summary>
    public static async Task<string> PrimaryAsync(AskwerkDbContext context, IInstanceSettingsRepository settings, HttpRequest request, Survey survey, CancellationToken cancellationToken, string? host = null)
    {
        var links = await OfAsync(context, settings, request, survey, cancellationToken);

        return (links.FirstOrDefault(link => string.Equals(link.Hostname, host, StringComparison.OrdinalIgnoreCase)) ?? links[0]).Url;
    }
}
