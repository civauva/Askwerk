// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Auditing;
using Askwerk.Application.Email;
using Askwerk.Application.Instances;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Instances;

namespace Askwerk.Application.Setup;

/// <summary>Input of the first-run setup (AWK-16, W2 steps 1–3).</summary>
/// <param name="OwnerName">Name of the instance owner.</param>
/// <param name="OwnerEmail">Email of the instance owner.</param>
/// <param name="OwnerPassword">Password of the instance owner.</param>
/// <param name="InstanceName">Instance name.</param>
/// <param name="BaseAddress">Base address for links.</param>
/// <param name="DefaultLanguage">Default language.</param>
/// <param name="TimeZone">IANA time zone.</param>
/// <param name="OrganisationName">Name of the first tenant.</param>
/// <param name="OrganisationSlug">Slug of the first tenant.</param>
/// <param name="Email">SMTP settings from step 4, or null when skipped.</param>
public sealed record CompleteSetupCommand(
    string OwnerName,
    string OwnerEmail,
    string OwnerPassword,
    string InstanceName,
    string BaseAddress,
    string DefaultLanguage,
    string TimeZone,
    string OrganisationName,
    string OrganisationSlug,
    SmtpSettings? Email = null);

/// <summary>What setup created.</summary>
/// <param name="UserId">The owner.</param>
/// <param name="TenantId">The first tenant.</param>
public sealed record SetupResult(Guid UserId, Guid TenantId);

/// <summary>Setup already ran; the setup endpoints then answer 404.</summary>
public sealed class SetupAlreadyCompletedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SetupAlreadyCompletedException()
        : base("Setup has already been completed.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public SetupAlreadyCompletedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public SetupAlreadyCompletedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Creates the owner, the first organisation and the instance settings in one audited transaction.</summary>
public sealed class CompleteSetupHandler(
    IInstanceRepository instances,
    IInstanceSettingsRepository settings,
    ITenantRepository tenants,
    IAccounts accounts,
    ISecretProtector secrets,
    IDataScope scope,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace)
{
    /// <summary>Runs setup once.</summary>
    /// <param name="command">The input.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created owner and tenant.</returns>
    public async Task<SetupResult> HandleAsync(CompleteSetupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await settings.FindAsync(cancellationToken) is not null)
        {
            throw new SetupAlreadyCompletedException();
        }

        var instance = await instances.FindAsync(cancellationToken)
            ?? throw new InvalidOperationException("The instance is initialised on first start, before setup can run.");
        var now = time.GetUtcNow();
        var user = User.Create(Guid.CreateVersion7(now), command.OwnerEmail, command.OwnerName, now);
        // Whoever sets up the installation administers it (AWK-38).
        user.SetInstallationAdmin(true);
        var tenant = Tenant.Create(Guid.CreateVersion7(now), command.OrganisationName, command.OrganisationSlug, now);

        if (await tenants.SlugTakenAsync(tenant.Slug, cancellationToken))
        {
            throw new DomainValidationException("slug", "slug-taken");
        }

        scope.UseUser(user.Id);
        var passwordError = await accounts.CreateAsync(user, command.OwnerPassword, cancellationToken);

        if (passwordError is not null)
        {
            throw new DomainValidationException("password", passwordError);
        }

        var email = command.Email;
        settings.Add(new InstanceSettings(
            instance.Id,
            command.InstanceName.Trim(),
            command.BaseAddress.Trim().TrimEnd('/'),
            command.DefaultLanguage,
            command.TimeZone,
            user.Id,
            now,
            email?.Host.Trim(),
            email?.Port,
            email?.UserName,
            email?.Password is { Length: > 0 } password ? secrets.Protect(password) : null,
            email?.Sender.Trim()));
        await AuditAsync(null, "setup.completed", "Instance", instance.Id, user.Id, now, cancellationToken);
        await AuditAsync(null, "user.created", "User", user.Id, user.Id, now, cancellationToken);

        // Tenant rows are written inside the new tenant, so row-level security accepts them.
        scope.UseTenant(tenant.Id);
        tenants.Add(tenant);
        var membership = new Membership(Guid.CreateVersion7(now), user.Id, tenant.Id, null, TenantRole.Owner, now);
        tenants.Add(membership);
        await AuditAsync(tenant.Id, "tenant.created", "Tenant", tenant.Id, user.Id, now, cancellationToken);
        await AuditAsync(tenant.Id, "membership.created", "Membership", membership.Id, user.Id, now, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetupResult(user.Id, tenant.Id);
    }

    private Task AuditAsync(Guid? tenantId, string action, string entityType, Guid entityId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(now, tenantId, AuditActorKind.User, actorId.ToString(), action, entityType, entityId.ToString(), Diff: null, trace.CurrentTraceId),
            cancellationToken);
}
