// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Configuration;
using Askwerk.Api.Identity;
using Askwerk.Application.Email;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Installation;

/// <summary>
/// Administration → This Askwerk (AWK-38, W29): every organisation on the installation, its administrators and its
/// settings. Installation administrators only (whoever ran setup, and those they add); everyone else gets 403.
/// Organisations are read and changed with the database owner's connection, across row-level security.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/installation")]
public sealed class InstallationController(
    AskwerkDbContext context,
    AskwerkSettings askwerk,
    IInstanceSettingsRepository instance,
    ISecretProtector secrets,
    EmailSettingsProvider email,
    IEmailSender sender,
    TenantStates states,
    TenantRemoval removal,
    InstallationOverrides overrides,
    TimeProvider time,
    ITraceContext trace) : ControllerBase
{
    /// <summary>The installation: its name, address, language, time zone, email and version, and what the configuration locks.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The installation; 403 for everyone but its administrators.</returns>
    [HttpGet]
    [ProducesResponseType<InstallationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var settings = await instance.FindAsync(cancellationToken);

        return Ok(new InstallationResponse(
            settings?.Name ?? "Askwerk",
            settings?.BaseAddress ?? $"{Request.Scheme}://{Request.Host}",
            settings?.DefaultLanguage ?? "en",
            settings?.TimeZone ?? "UTC",
            settings?.SmtpHost is not null,
            askwerk.Version,
            settings?.SmtpHost,
            settings?.SmtpPort,
            settings?.SmtpUserName,
            settings?.SmtpPassword is not null,
            settings?.SmtpSender,
            overrides.Locked));
    }

    /// <summary>
    /// Saves the installation's name, defaults and email (AWK-39, W30). What the configuration locks keeps its value; an
    /// empty server turns email off; a null password keeps the stored one.
    /// </summary>
    /// <param name="request">The settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 at the field; 403.</returns>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Change(ChangeInstallationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var name = request.Name?.Trim() ?? "";
        var language = request.DefaultLanguage?.Trim() ?? "";
        var timeZone = request.TimeZone?.Trim() ?? "";
        var host = request.SmtpHost?.Trim() ?? "";
        var sender = request.SmtpSender?.Trim() ?? "";
        var port = request.SmtpPort ?? 587;

        if (name.Length is 0 or > 200)
        {
            ModelState.AddModelError("name", "name-required");
        }

        if (language is not ("de" or "en"))
        {
            ModelState.AddModelError("defaultLanguage", "language-invalid");
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
        {
            ModelState.AddModelError("timeZone", "time-zone-invalid");
        }

        if (host.Length > 0 && !overrides.LocksEmail)
        {
            if (port is < 1 or > 65535)
            {
                ModelState.AddModelError("smtpPort", "port-invalid");
            }

            if (!System.Net.Mail.MailAddress.TryCreate(sender, out _))
            {
                ModelState.AddModelError("smtpSender", "sender-invalid");
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var stored = await owner.InstanceSettings.FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return NotFound();
        }

        var changed = stored with
        {
            Name = overrides.Name is null ? name : stored.Name,
            DefaultLanguage = overrides.DefaultLanguage is null ? language : stored.DefaultLanguage,
            TimeZone = overrides.TimeZone is null ? timeZone : stored.TimeZone,
        };

        if (!overrides.LocksEmail)
        {
            var user = string.IsNullOrWhiteSpace(request.SmtpUserName) ? null : request.SmtpUserName.Trim();
            changed = host.Length == 0
                ? changed with { SmtpHost = null, SmtpPort = null, SmtpUserName = null, SmtpPassword = null, SmtpSender = null }
                : changed with
                {
                    SmtpHost = host,
                    SmtpPort = port,
                    SmtpUserName = user,
                    SmtpPassword = user is null ? null : request.SmtpPassword is null ? stored.SmtpPassword : request.SmtpPassword.Length == 0 ? null : secrets.Protect(request.SmtpPassword),
                    SmtpSender = sender,
                };
        }

        owner.Entry(stored).CurrentValues.SetValues(changed);
        var diff = JsonSerializer.Serialize(new { changed.Name, changed.DefaultLanguage, changed.TimeZone, changed.SmtpHost, changed.SmtpPort, changed.SmtpUserName, changed.SmtpSender, passwordChanged = changed.SmtpPassword != stored.SmtpPassword });
        await new EfAuditWriter(owner).WriteAsync(new AuditContent(time.GetUtcNow(), null, AuditActorKind.User, User.UserId().ToString(), "installation.changed", "Instance", stored.InstanceId.ToString(), diff, trace.CurrentTraceId), cancellationToken);
        await new EfUnitOfWork(owner).SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Sends a test email to the signed-in administrator with the email settings as typed (AWK-39); a null password uses the
    /// stored one. When the configuration locks email, it tests those settings.
    /// </summary>
    /// <param name="request">The email settings to try.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the address it went to; 400 smtp-failed; 403.</returns>
    [HttpPost("test-email")]
    [ProducesResponseType<TestEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TestEmail(InstallationEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var userId = User.UserId();
        var to = await context.Users.Where(user => user.Id == userId).Select(user => user.Email).FirstAsync(cancellationToken);
        var effective = await instance.FindAsync(cancellationToken);
        var stored = effective?.SmtpPassword is null ? null : secrets.Unprotect(effective.SmtpPassword);
        var smtp = overrides.LocksEmail && effective?.SmtpHost is not null && effective.SmtpSender is not null
            ? new SmtpSettings(effective.SmtpHost, effective.SmtpPort ?? 587, effective.SmtpUserName, stored, effective.SmtpSender)
            : new SmtpSettings(request.Host?.Trim() ?? "", request.Port ?? 587, string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim(), request.Password ?? stored, request.Sender?.Trim() ?? "");

        try
        {
            await sender.SendAsync(smtp, EmailTexts.Test(to, effective?.DefaultLanguage ?? "en"), cancellationToken);
        }
        catch (Exception error) when (error is System.Net.Mail.SmtpException or FormatException or InvalidOperationException or ArgumentException or EmailRejectedException)
        {
            ModelState.AddModelError("smtp", "smtp-failed");

            return ValidationProblem(ModelState);
        }

        return Ok(new TestEmailResponse(to));
    }

    /// <summary>Every organisation whose data is not removed, with its owner, size and status, the newest last.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The organisations; 403 for everyone but installation administrators.</returns>
    [HttpGet("organisations")]
    [ProducesResponseType<InstallationOrganisationList>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Organisations(CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var since = time.GetUtcNow().AddDays(-30);
        var rows = await owner.Tenants.AsNoTracking()
            .Where(tenant => tenant.PurgedAt == null)
            .OrderBy(tenant => tenant.CreatedAt)
            .Select(tenant => new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                tenant.CreatedAt,
                tenant.Status,
                tenant.DeletedAt,
                Owner = owner.Memberships.Where(membership => membership.TenantId == tenant.Id && membership.Role == TenantRole.Owner)
                    .Join(owner.Users, membership => membership.UserId, user => user.Id, (membership, user) => user.DisplayName)
                    .FirstOrDefault(),
                Members = owner.Memberships.Count(membership => membership.TenantId == tenant.Id),
                Surveys = owner.Surveys.Count(survey => survey.TenantId == tenant.Id && survey.DeletedAt == null),
                Responses = owner.Responses.Count(response => response.TenantId == tenant.Id),
                Recent = owner.Responses.Count(response => response.TenantId == tenant.Id && response.StartedAt >= since),
            })
            .ToListAsync(cancellationToken);

        return Ok(new InstallationOrganisationList(
            [.. rows.Select(row => new InstallationOrganisation(row.Id, row.Name, row.Slug, row.Owner, row.Members, row.Surveys, row.Responses, row.CreatedAt, row.Status.ToString().ToLowerInvariant(), row.Status == TenantStatus.Deleted ? row.DeletedAt + Tenant.GracePeriod : null))],
            rows.Where(row => row.Status != TenantStatus.Deleted).Sum(row => row.Members),
            rows.Where(row => row.Status != TenantStatus.Deleted).Sum(row => row.Surveys),
            rows.Sum(row => row.Recent)));
    }

    /// <summary>
    /// Creates an organisation with its first owner: an existing account joins at once; anyone else is invited, by email
    /// when it is set up, and the answer carries the link either way.
    /// </summary>
    /// <param name="request">Name, short name and the owner's email.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the id and, for an invitation, its link; 400 at the field; 403 for everyone but installation administrators.</returns>
    [HttpPost("organisations")]
    [ProducesResponseType<CreatedOrganisationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateOrganisationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var name = request.Name?.Trim() ?? "";
        var slug = (string.IsNullOrWhiteSpace(request.Slug) ? Tenant.SuggestSlug(name) : request.Slug).Trim().ToLowerInvariant();
        var ownerEmail = MemberInvitation.Normalise(request.OwnerEmail ?? "");
        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);

        if (name.Length == 0)
        {
            ModelState.AddModelError("name", "name-required");
        }

        if (!Tenant.IsValidSlug(slug))
        {
            ModelState.AddModelError("slug", "slug-invalid");
        }
        else if (await owner.Tenants.AnyAsync(tenant => tenant.Slug == slug, cancellationToken))
        {
            ModelState.AddModelError("slug", "slug-taken");
        }

        if (!ownerEmail.Contains('@', StringComparison.Ordinal))
        {
            ModelState.AddModelError("ownerEmail", "email-invalid");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        var me = User.UserId()!.Value;
        var tenant = Tenant.Create(Guid.CreateVersion7(now), name, slug, now);
        owner.Tenants.Add(tenant);
        var normalizedOwner = ownerEmail.ToUpperInvariant();
        var existing = await owner.Users.FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedOwner && user.DeactivatedAt == null, cancellationToken);
        string? link = null;
        MemberInvitation? invitation = null;

        if (existing is not null)
        {
            owner.Memberships.Add(new Membership(Guid.CreateVersion7(now), existing.Id, tenant.Id, null, TenantRole.Owner, now));
        }
        else
        {
            var token = MemberInvitation.NewToken();
            invitation = MemberInvitation.Create(Guid.CreateVersion7(now), tenant.Id, ownerEmail, TenantRole.Owner, token, secrets.Protect(token), me, now);
            owner.MemberInvitations.Add(invitation);
            var baseAddress = (await instance.FindAsync(cancellationToken))?.BaseAddress ?? $"{Request.Scheme}://{Request.Host}";
            link = $"{baseAddress.TrimEnd('/')}/join/{token}";
        }

        await new EfAuditWriter(owner).WriteAsync(new AuditContent(now, tenant.Id, AuditActorKind.User, me.ToString(), "organisation.created", "Tenant", tenant.Id.ToString(), JsonSerializer.Serialize(new { name, slug, owner = ownerEmail }), trace.CurrentTraceId), cancellationToken);
        await new EfUnitOfWork(owner).SaveChangesAsync(cancellationToken);
        var sent = false;

        if (invitation is not null && link is not null && await email.FindAsync(cancellationToken) is { } found)
        {
            try
            {
                var inviter = await context.Users.Where(user => user.Id == me).Select(user => user.DisplayName).FirstAsync(cancellationToken);
                await sender.SendAsync(found.Smtp, EmailTexts.MemberInvitation(invitation.Email, name, inviter, link, found.Language), cancellationToken);
                invitation.MarkSent(now);
                await owner.SaveChangesAsync(cancellationToken);
                sent = true;
            }
            catch (EmailRejectedException)
            {
                sent = false;
            }
        }

        return Created($"/api/app/installation/organisations/{tenant.Id}", new CreatedOrganisationResponse(tenant.Id, existing is not null, link, sent));
    }

    /// <summary>Closes an organisation to its members, keys and survey links until it is resumed.</summary>
    /// <param name="id">The organisation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpPost("organisations/{id:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken) => ChangeAsync(id, "organisation.suspended", tenant => tenant.Suspend(), cancellationToken);

    /// <summary>Opens a suspended organisation again.</summary>
    /// <param name="id">The organisation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpPost("organisations/{id:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken) => ChangeAsync(id, "organisation.resumed", tenant => tenant.Resume(), cancellationToken);

    /// <summary>Deletes an organisation: closed at once, its data removed after 30 days unless restored (AWK-39).</summary>
    /// <param name="id">The organisation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpDelete("organisations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) => ChangeAsync(id, "organisation.deleted", tenant => tenant.Delete(time.GetUtcNow()), cancellationToken);

    /// <summary>Opens a deleted organisation again, as it was, while its data is still there (AWK-39).</summary>
    /// <param name="id">The organisation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404 when it is not deleted or its data is removed.</returns>
    [HttpPost("organisations/{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var tenant = await owner.Tenants.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (tenant is null || !tenant.Restore())
        {
            return NotFound();
        }

        await new EfAuditWriter(owner).WriteAsync(new AuditContent(time.GetUtcNow(), tenant.Id, AuditActorKind.User, User.UserId().ToString(), "organisation.restored", "Tenant", tenant.Id.ToString(), null, trace.CurrentTraceId), cancellationToken);
        await new EfUnitOfWork(owner).SaveChangesAsync(cancellationToken);
        states.Changed(tenant.Id);

        return NoContent();
    }

    /// <summary>What removing a deleted organisation's data takes away, for the confirmation (AWK-39).</summary>
    /// <param name="id">The organisation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The counts; 403; 404 when it is not deleted or its data is removed.</returns>
    [HttpGet("organisations/{id:guid}/removal")]
    [ProducesResponseType<RemovalCounts>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Removal(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        return await removal.CountAsync(id, cancellationToken) is { } counts ? Ok(counts) : NotFound();
    }

    /// <summary>Removes a deleted organisation's data now, for good; the short name confirms it (AWK-39).</summary>
    /// <param name="id">The organisation.</param>
    /// <param name="request">The organisation's short name, typed to confirm.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 slug-mismatch; 403; 404 when it is not deleted or its data is removed.</returns>
    [HttpPost("organisations/{id:guid}/remove")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, RemoveOrganisationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        await using (var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString))
        {
            var slug = await owner.Tenants.Where(row => row.Id == id && row.Status == TenantStatus.Deleted && row.PurgedAt == null).Select(row => row.Slug).FirstOrDefaultAsync(cancellationToken);

            if (slug is null)
            {
                return NotFound();
            }

            if (!string.Equals(request.Slug?.Trim(), slug, StringComparison.Ordinal))
            {
                ModelState.AddModelError("slug", "slug-mismatch");

                return ValidationProblem(ModelState);
            }
        }

        return await removal.RemoveAsync(id, User.UserId(), trace.CurrentTraceId, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>The installation's administrators.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The administrators; 403.</returns>
    [HttpGet("administrators")]
    [ProducesResponseType<InstallationAdministratorList>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Administrators(CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var me = User.UserId();
        var admins = await owner.Users.AsNoTracking().Where(user => user.IsInstallationAdmin && user.DeactivatedAt == null).OrderBy(user => user.DisplayName).ToListAsync(cancellationToken);

        return Ok(new InstallationAdministratorList([.. admins.Select(user => new InstallationAdministrator(user.Id, user.DisplayName, user.Email, user.Id == me))]));
    }

    /// <summary>Makes an existing account an installation administrator.</summary>
    /// <param name="request">The account's email.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404 user-not-found.</returns>
    [HttpPost("administrators")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<OrganisationProblemResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddAdministrator(AddAdministratorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var normalized = MemberInvitation.Normalise(request.Email ?? "").ToUpperInvariant();
        var user = await owner.Users.FirstOrDefaultAsync(row => row.NormalizedEmail == normalized && row.DeactivatedAt == null, cancellationToken);

        if (user is null)
        {
            return NotFound(new OrganisationProblemResponse("user-not-found"));
        }

        user.SetInstallationAdmin(true);
        await new EfAuditWriter(owner).WriteAsync(new AuditContent(time.GetUtcNow(), null, AuditActorKind.User, User.UserId().ToString(), "installation.admin-added", "User", user.Id.ToString(), null, trace.CurrentTraceId), cancellationToken);
        await new EfUnitOfWork(owner).SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Takes the role away; the last administrator stays.</summary>
    /// <param name="userId">The administrator.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404; 409 last-admin.</returns>
    [HttpDelete("administrators/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<OrganisationProblemResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveAdministrator(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var user = await owner.Users.FirstOrDefaultAsync(row => row.Id == userId && row.IsInstallationAdmin, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (await owner.Users.CountAsync(row => row.IsInstallationAdmin && row.DeactivatedAt == null, cancellationToken) <= 1)
        {
            return Conflict(new OrganisationProblemResponse("last-admin"));
        }

        user.SetInstallationAdmin(false);
        await new EfAuditWriter(owner).WriteAsync(new AuditContent(time.GetUtcNow(), null, AuditActorKind.User, User.UserId().ToString(), "installation.admin-removed", "User", user.Id.ToString(), null, trace.CurrentTraceId), cancellationToken);
        await new EfUnitOfWork(owner).SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult> ChangeAsync(Guid id, string action, Action<Tenant> change, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var tenant = await owner.Tenants.FirstOrDefaultAsync(row => row.Id == id && row.Status != TenantStatus.Deleted, cancellationToken);

        if (tenant is null)
        {
            return NotFound();
        }

        change(tenant);
        await new EfAuditWriter(owner).WriteAsync(new AuditContent(time.GetUtcNow(), tenant.Id, AuditActorKind.User, User.UserId().ToString(), action, "Tenant", tenant.Id.ToString(), null, trace.CurrentTraceId), cancellationToken);
        await new EfUnitOfWork(owner).SaveChangesAsync(cancellationToken);
        states.Changed(tenant.Id);

        return NoContent();
    }

    private async Task<bool> IsAdminAsync(CancellationToken cancellationToken)
    {
        var userId = User.UserId();

        return userId is not null && await context.Users.AnyAsync(user => user.Id == userId && user.IsInstallationAdmin && user.DeactivatedAt == null, cancellationToken);
    }
}

/// <summary>The installation (AWK-38).</summary>
/// <param name="Name">Its name.</param>
/// <param name="BaseAddress">Its address.</param>
/// <param name="DefaultLanguage">Language for new organisations and surveys.</param>
/// <param name="TimeZone">Its time zone.</param>
/// <param name="EmailConfigured">Whether email is set up.</param>
/// <param name="Version">The running version.</param>
/// <param name="SmtpHost">SMTP server; null while email is off (AWK-39).</param>
/// <param name="SmtpPort">SMTP port.</param>
/// <param name="SmtpUserName">SMTP login.</param>
/// <param name="SmtpPasswordSet">Whether a password is stored; it is never sent back.</param>
/// <param name="SmtpSender">From address.</param>
/// <param name="Locked">What the configuration fixes: baseAddress always, and name, defaultLanguage, timeZone or email.</param>
public sealed record InstallationResponse(
    string Name,
    string BaseAddress,
    string DefaultLanguage,
    string TimeZone,
    bool EmailConfigured,
    string Version,
    string? SmtpHost = null,
    int? SmtpPort = null,
    string? SmtpUserName = null,
    bool SmtpPasswordSet = false,
    string? SmtpSender = null,
    IReadOnlyList<string>? Locked = null);

/// <summary>The installation's settings to save (AWK-39).</summary>
/// <param name="Name">Its name.</param>
/// <param name="DefaultLanguage">de or en.</param>
/// <param name="TimeZone">An IANA time zone.</param>
/// <param name="SmtpHost">SMTP server; empty turns email off.</param>
/// <param name="SmtpPort">SMTP port; 587 when not given.</param>
/// <param name="SmtpUserName">SMTP login, if any.</param>
/// <param name="SmtpPassword">New password; null keeps the stored one, empty removes it.</param>
/// <param name="SmtpSender">From address.</param>
public sealed record ChangeInstallationRequest(string? Name, string? DefaultLanguage, string? TimeZone, string? SmtpHost, int? SmtpPort, string? SmtpUserName, string? SmtpPassword, string? SmtpSender);

/// <summary>Email settings to try (AWK-39).</summary>
/// <param name="Host">SMTP server.</param>
/// <param name="Port">Port; 587 when not given.</param>
/// <param name="UserName">Login, if any.</param>
/// <param name="Password">Password; null uses the stored one.</param>
/// <param name="Sender">From address.</param>
public sealed record InstallationEmailRequest(string? Host, int? Port, string? UserName, string? Password, string? Sender);

/// <summary>Where the test email went.</summary>
/// <param name="To">The administrator's address.</param>
public sealed record TestEmailResponse(string To);

/// <summary>Every organisation, with totals.</summary>
/// <param name="Items">The organisations, oldest first.</param>
/// <param name="Members">Members across them.</param>
/// <param name="Surveys">Surveys across them.</param>
/// <param name="RecentResponses">Responses started in the last 30 days.</param>
public sealed record InstallationOrganisationList(IReadOnlyList<InstallationOrganisation> Items, int Members, int Surveys, int RecentResponses);

/// <summary>One organisation of the installation.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Slug">Its short name.</param>
/// <param name="Owner">Its first owner's name; null while the owner has not joined.</param>
/// <param name="Members">Members.</param>
/// <param name="Surveys">Surveys.</param>
/// <param name="Responses">Responses.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="Status">active, suspended or deleted.</param>
/// <param name="RemovesAt">For a deleted organisation, when its data is removed (AWK-39).</param>
public sealed record InstallationOrganisation(Guid Id, string Name, string Slug, string? Owner, int Members, int Surveys, int Responses, DateTimeOffset CreatedAt, string Status, DateTimeOffset? RemovesAt = null);

/// <summary>Confirms removing an organisation's data (AWK-39).</summary>
/// <param name="Slug">The organisation's short name, typed.</param>
public sealed record RemoveOrganisationRequest(string? Slug);

/// <summary>A new organisation.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Slug">Its short name; made from the name when not given.</param>
/// <param name="OwnerEmail">The first owner's email.</param>
public sealed record CreateOrganisationRequest(string? Name, string? Slug, string? OwnerEmail);

/// <summary>The organisation made.</summary>
/// <param name="Id">Its id.</param>
/// <param name="OwnerJoined">Whether the owner had an account and is already its owner.</param>
/// <param name="InvitationLink">Otherwise the link that makes them the owner.</param>
/// <param name="InvitationSent">Whether the link went out by email.</param>
public sealed record CreatedOrganisationResponse(Guid Id, bool OwnerJoined, string? InvitationLink, bool InvitationSent);

/// <summary>The installation's administrators.</summary>
/// <param name="Items">They, by name.</param>
public sealed record InstallationAdministratorList(IReadOnlyList<InstallationAdministrator> Items);

/// <summary>One installation administrator.</summary>
/// <param name="Id">User id.</param>
/// <param name="Name">Name.</param>
/// <param name="Email">Email.</param>
/// <param name="Me">Whether it is the signed-in user.</param>
public sealed record InstallationAdministrator(Guid Id, string Name, string Email, bool Me);

/// <summary>An account to make an installation administrator.</summary>
/// <param name="Email">Its email.</param>
public sealed record AddAdministratorRequest(string? Email);

/// <summary>Why a request about the installation was refused.</summary>
/// <param name="Code">Stable code.</param>
public sealed record OrganisationProblemResponse(string Code);
