// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Identity;
using Askwerk.Api.Startup;
using Askwerk.Application.Auditing;
using Askwerk.Application.Domains;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Domains;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Askwerk.Api.App.Domains;

/// <summary>
/// Settings → Domains (AWK-7, W12 A): the system address and the organisation's own domains, verified by DNS, one of
/// them primary. Everyone in the organisation sees them; owners and admins change them, and every change is audited.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/domains")]
public sealed class DomainsController(
    AskwerkDbContext context,
    IInstanceSettingsRepository settings,
    IDnsLookup dns,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Managers = [TenantRole.Owner, TenantRole.Admin];

    /// <summary>The system address and the organisation's domains, newest first.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The domains.</returns>
    [HttpGet]
    [ProducesResponseType<DomainsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DomainsResponse>> List(CancellationToken cancellationToken)
    {
        var system = await SystemHostAsync(cancellationToken);
        var domains = await context.Domains.OrderByDescending(domain => domain.CreatedAt).ToListAsync(cancellationToken);

        return new DomainsResponse(new SystemDomain(system, !domains.Any(domain => domain.IsPrimary)), [.. domains.Select(domain => Item(domain, system))]);
    }

    /// <summary>Adds a domain; it waits for its two DNS records.</summary>
    /// <param name="request">The hostname, e.g. surveys.acme.at.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the records to set; 400 at the hostname; 403 for other roles; 409 when another organisation or this one has it.</returns>
    [HttpPost]
    [ProducesResponseType<DomainItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add(AddDomainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var system = await SystemHostAsync(cancellationToken);

        if (CustomDomain.Normalize(request.Hostname, system, out var hostname) is { } error)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["hostname"] = [error] }));
        }

        var domain = CustomDomain.Add(Guid.CreateVersion7(time.GetUtcNow()), User.TenantId()!.Value, hostname, time.GetUtcNow());
        context.Domains.Add(domain);
        await AuditAsync(domain, "domain.added", cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(StatusCodes.Status409Conflict, "domain-taken");
        }

        return StatusCode(StatusCodes.Status201Created, Item(domain, system));
    }

    /// <summary>Checks the domain's DNS records now.</summary>
    /// <param name="id">The domain.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The domain with what was found; 403 for other roles; 404 when it is not this organisation's.</returns>
    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType<DomainItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var domain = await context.Domains.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (domain is null)
        {
            return NotFound();
        }

        var system = await SystemHostAsync(cancellationToken);

        if (await DomainChecks.CheckAsync(domain, system, dns, time, cancellationToken))
        {
            await AuditAsync(domain, "domain.verified", cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(Item(domain, system));
    }

    /// <summary>Makes a verified domain primary, or the system address with no domain.</summary>
    /// <param name="request">The domain; null for the system address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for other roles; 404 for another organisation's domain; 409 when it is not verified.</returns>
    [HttpPut("primary")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Primary(PrimaryDomainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var domains = await context.Domains.ToListAsync(cancellationToken);
        var chosen = request.DomainId is { } id ? domains.FirstOrDefault(domain => domain.Id == id) : null;

        if (request.DomainId is not null && chosen is null)
        {
            return NotFound();
        }

        if (chosen is { Status: not DomainStatus.Verified })
        {
            return Problem(StatusCodes.Status409Conflict, "domain-not-verified");
        }

        foreach (var domain in domains)
        {
            domain.SetPrimary(domain == chosen);
        }

        await audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), User.TenantId(), AuditActorKind.User, User.UserId().ToString(), "domain.primary-chosen", "Domain", chosen?.Hostname ?? "system", Diff: null, trace.CurrentTraceId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Removes a domain; links on it stop working. Without a primary domain the system address is primary.</summary>
    /// <param name="id">The domain.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for other roles; 404 when it is not this organisation's.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var domain = await context.Domains.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (domain is null)
        {
            return NotFound();
        }

        context.Domains.Remove(domain);
        await AuditAsync(domain, "domain.removed", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    internal static DomainItem Item(CustomDomain domain, string system) => new(
        domain.Id,
        domain.Hostname,
        domain.Status.ToString().ToLowerInvariant(),
        domain.IsPrimary,
        system,
        domain.TxtName,
        domain.TxtValue,
        domain.CnameFound,
        domain.TxtFound,
        domain.CreatedAt,
        domain.CheckedAt,
        domain.VerifiedAt);

    private async Task<string> SystemHostAsync(CancellationToken cancellationToken) =>
        await settings.FindAsync(cancellationToken) is { } instance && Uri.TryCreate(instance.BaseAddress, UriKind.Absolute, out var address)
            ? address.Host.ToLowerInvariant()
            : Request.Host.Host.ToLowerInvariant();

    private ObjectResult Problem(int status, string code) =>
        StatusCode(status, new DomainProblem(code, System.Diagnostics.Activity.Current?.TraceId.ToHexString()));

    /// <summary>Null when the user may change domains; otherwise the answer to give.</summary>
    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Managers.Contains(known) ? null : Problem(StatusCodes.Status403Forbidden, "role-not-allowed");
    }

    private Task AuditAsync(CustomDomain domain, string action, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), domain.TenantId, AuditActorKind.User, User.UserId().ToString(), action, "Domain", domain.Hostname, Diff: null, trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>
/// Caddy's on-demand TLS asks before it issues a certificate (architecture.md): yes for the system address and for
/// verified domains, no for anything else, so nobody can make the server request certificates for other names.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[AllowAnonymous]
[Route("api/caddy")]
public sealed class CaddyController(AskwerkDbContext context, IInstanceSettingsRepository settings) : ControllerBase
{
    /// <summary>Whether Caddy may get a certificate for a name.</summary>
    /// <param name="domain">The name Caddy was asked for.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 when Askwerk serves it; 404 otherwise.</returns>
    [HttpGet("ask")]
    public async Task<IActionResult> Ask(string? domain, CancellationToken cancellationToken)
    {
        var name = (domain ?? "").Trim().TrimEnd('.').ToLowerInvariant();

        if (name.Length is 0 or > 253)
        {
            return NotFound();
        }

        var system = await settings.FindAsync(cancellationToken) is { } instance && Uri.TryCreate(instance.BaseAddress, UriKind.Absolute, out var address) ? address.Host : null;
        var served = string.Equals(name, system, StringComparison.OrdinalIgnoreCase)
            || await context.Database.SqlQuery<bool>($"SELECT domain_is_served({name}) AS \"Value\"").SingleAsync(cancellationToken);

        return served ? Ok() : NotFound();
    }
}

/// <summary>
/// Checks pending domains by themselves (W12 A, note 2): every five minutes for seven days, then gives up. Runs per
/// organisation, under its row-level security.
/// </summary>
public sealed partial class DomainChecks(IServiceScopeFactory scopes, DatabaseState state, TimeProvider time, ILogger<DomainChecks> logger)
{
    /// <summary>Checks a domain's records and records the result.</summary>
    /// <param name="domain">The domain.</param>
    /// <param name="system">The instance's host, the CNAME's target.</param>
    /// <param name="dns">DNS.</param>
    /// <param name="time">The clock.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Whether it was verified now.</returns>
    public static async Task<bool> CheckAsync(CustomDomain domain, string system, IDnsLookup dns, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(dns);
        ArgumentNullException.ThrowIfNull(time);
        var cname = await dns.CnameAsync(domain.Hostname, cancellationToken);
        var texts = await dns.TxtAsync(domain.TxtName, cancellationToken);

        return domain.Checked(
            string.Equals(cname?.TrimEnd('.'), system, StringComparison.OrdinalIgnoreCase),
            texts.Any(text => text.Trim() == domain.TxtValue),
            time.GetUtcNow());
    }

    /// <summary>One round over all pending domains of all organisations.</summary>
    /// <param name="cancellationToken">Cancels the round.</param>
    /// <returns>When done.</returns>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!state.IsReady)
        {
            return;
        }

        List<PendingDomain> pending;

        using (var scope = scopes.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AskwerkDbContext>();
            pending = await context.Database.SqlQuery<PendingDomain>($"SELECT id AS \"Id\", tenant_id AS \"TenantId\" FROM pending_domains()").ToListAsync(cancellationToken);
        }

        foreach (var item in pending)
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<DbSessionContext>().TenantId = item.TenantId;
            var context = scope.ServiceProvider.GetRequiredService<AskwerkDbContext>();
            var settings = scope.ServiceProvider.GetRequiredService<IInstanceSettingsRepository>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            var domain = await context.Domains.SingleAsync(row => row.Id == item.Id, cancellationToken);
            var system = await settings.FindAsync(cancellationToken) is { } instance && Uri.TryCreate(instance.BaseAddress, UriKind.Absolute, out var address) ? address.Host : "";
            string? action = null;

            if (domain.GiveUpIfLate(time.GetUtcNow()))
            {
                action = "domain.failed";
            }
            else if (await CheckAsync(domain, system, scope.ServiceProvider.GetRequiredService<IDnsLookup>(), time, cancellationToken))
            {
                action = "domain.verified";
            }

            if (action is not null)
            {
                await audit.WriteAsync(new AuditContent(time.GetUtcNow(), domain.TenantId, AuditActorKind.System, null, action, "Domain", domain.Hostname, Diff: null, TraceId: null), cancellationToken);
                LogChecked(logger, domain.Id, action);
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Domain {DomainId}: {Action}")]
    private static partial void LogChecked(ILogger logger, Guid domainId, string action);

    private sealed class PendingDomain
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }
    }
}

/// <summary>Runs <see cref="DomainChecks"/> every five minutes.</summary>
public sealed partial class DomainChecksService(DomainChecks checks, TimeProvider time, ILogger<DomainChecksService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await checks.RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The domain checks failed; they run again in five minutes.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>The domains of an organisation.</summary>
/// <param name="System">The instance's own address, always there.</param>
/// <param name="Items">The organisation's domains, newest first.</param>
public sealed record DomainsResponse(SystemDomain System, IReadOnlyList<DomainItem> Items);

/// <summary>The instance's own address.</summary>
/// <param name="Hostname">E.g. surveys.askwerk.app.</param>
/// <param name="IsPrimary">Whether it is the organisation's primary address (no domain is).</param>
public sealed record SystemDomain(string Hostname, bool IsPrimary);

/// <summary>A domain of the organisation.</summary>
/// <param name="Id">The domain.</param>
/// <param name="Hostname">E.g. surveys.acme.at.</param>
/// <param name="Status">pending, verified or failed.</param>
/// <param name="IsPrimary">Whether links, QR codes and invitations use it.</param>
/// <param name="CnameTarget">What the CNAME must point to.</param>
/// <param name="TxtName">The TXT record's name.</param>
/// <param name="TxtValue">The TXT record's value.</param>
/// <param name="CnameFound">The CNAME was right at the last check.</param>
/// <param name="TxtFound">The TXT record was right at the last check.</param>
/// <param name="CreatedAt">Added (UTC).</param>
/// <param name="CheckedAt">Last check (UTC).</param>
/// <param name="VerifiedAt">Verified (UTC).</param>
public sealed record DomainItem(Guid Id, string Hostname, string Status, bool IsPrimary, string CnameTarget, string TxtName, string TxtValue, bool CnameFound, bool TxtFound, DateTimeOffset CreatedAt, DateTimeOffset? CheckedAt, DateTimeOffset? VerifiedAt);

/// <summary>A domain to add.</summary>
/// <param name="Hostname">E.g. surveys.acme.at.</param>
public sealed record AddDomainRequest(string Hostname);

/// <summary>The primary domain to choose.</summary>
/// <param name="DomainId">A verified domain; null for the system address.</param>
public sealed record PrimaryDomainRequest(Guid? DomainId);

/// <summary>Why a domain request was refused.</summary>
/// <param name="Code">role-not-allowed, domain-taken or domain-not-verified.</param>
/// <param name="TraceId">The request's trace.</param>
public sealed record DomainProblem(string Code, string? TraceId);
