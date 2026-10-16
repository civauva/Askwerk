// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Instances;
using Askwerk.Tests.Application.Fakes;

namespace Askwerk.Tests.Application.Setup;

public sealed class CompleteSetupHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 21, 30, 0, TimeSpan.Zero);
    private static readonly Instance Instance = new(Guid.Parse("0199e000-0000-7000-8000-000000000001"), Now);

    private static CompleteSetupCommand Command(string slug = "acme", string password = "correct horse battery") => new(
        OwnerName: "Anna Berger",
        OwnerEmail: "anna@acme.at",
        OwnerPassword: password,
        InstanceName: "Acme Surveys",
        BaseAddress: "https://surveys.acme.at",
        DefaultLanguage: "de",
        TimeZone: "Europe/Vienna",
        OrganisationName: "Acme GmbH",
        OrganisationSlug: slug);

    private sealed class Setup
    {
        public InMemoryInstanceRepository Instances { get; } = new();

        public InMemoryStore Store { get; } = new();

        public RecordingAuditWriter Audit { get; } = new();

        public CompleteSetupHandler Handler { get; }

        public Setup()
        {
            Instances.Saved.Add(Instance);
            Handler = new CompleteSetupHandler(
                Instances,
                Store,
                Store,
                Store,
                new PrefixProtector(),
                Store,
                Audit,
                new CommitAll(Store, Audit),
                new FixedTimeProvider(Now),
                new FixedTraceContext("4bf92f3577b34da6a3ce929d0e0e4736"));
        }
    }

    private sealed class PrefixProtector : ISecretProtector
    {
        public string Protect(string secret) => "protected:" + secret;

        public string Unprotect(string protectedSecret) => protectedSecret["protected:".Length..];
    }

    private sealed class CommitAll(InMemoryStore store, RecordingAuditWriter audit) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            store.Commit();
            audit.Written.AddRange(audit.Pending);
            audit.Pending.Clear();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HandleAsync_FreshInstance_CreatesTheOwnerTheOrganisationAndTheirMembership()
    {
        var setup = new Setup();

        var result = await setup.Handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        var user = Assert.Single(setup.Store.SavedOf<User>());
        var tenant = Assert.Single(setup.Store.SavedOf<Tenant>());
        var membership = Assert.Single(setup.Store.SavedOf<Membership>());
        Assert.Equal("anna@acme.at", user.Email);
        Assert.Equal("hashed:correct horse battery", user.PasswordHash);
        Assert.Equal("acme", tenant.Slug);
        Assert.Equal((user.Id, tenant.Id, TenantRole.Owner), (membership.UserId, membership.TenantId, membership.Role));
        Assert.Equal(new SetupResult(user.Id, tenant.Id), result);
    }

    [Fact]
    public async Task HandleAsync_FreshInstance_RecordsTheInstanceSettings()
    {
        var setup = new Setup();

        var result = await setup.Handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        var settings = Assert.Single(setup.Store.SavedOf<InstanceSettings>());
        Assert.Equal(new InstanceSettings(Instance.Id, "Acme Surveys", "https://surveys.acme.at", "de", "Europe/Vienna", result.UserId, Now), settings);
    }

    [Fact]
    public async Task HandleAsync_FreshInstance_WritesTheTenantRowsInsideTheNewTenant()
    {
        var setup = new Setup();

        var result = await setup.Handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        var tenant = setup.Store.SavedOf<Tenant>().Single();
        var membership = setup.Store.SavedOf<Membership>().Single();
        Assert.Equal(result.TenantId, setup.Store.TenantInScopeWhenAdded[tenant]);
        Assert.Equal(result.TenantId, setup.Store.TenantInScopeWhenAdded[membership]);
    }

    [Fact]
    public async Task HandleAsync_FreshInstance_AuditsOnTheInstanceAndTheTenantChain()
    {
        var setup = new Setup();

        var result = await setup.Handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                ("setup.completed", (Guid?)null),
                ("user.created", null),
                ("tenant.created", result.TenantId),
                ("membership.created", result.TenantId),
            ],
            setup.Audit.Written.Select(entry => (entry.Action, entry.TenantId)));
        Assert.All(setup.Audit.Written, entry => Assert.Equal((AuditActorKind.User, result.UserId.ToString()), (entry.ActorKind, entry.ActorId)));
    }

    [Fact]
    public async Task HandleAsync_SetupAlreadyCompleted_IsRefusedAndChangesNothing()
    {
        var setup = new Setup();
        await setup.Handler.HandleAsync(Command(), TestContext.Current.CancellationToken);
        setup.Audit.Written.Clear();

        await Assert.ThrowsAsync<SetupAlreadyCompletedException>(() => setup.Handler.HandleAsync(Command("acme-2"), TestContext.Current.CancellationToken));

        Assert.Single(setup.Store.SavedOf<Tenant>());
        Assert.Empty(setup.Audit.Written);
    }

    [Fact]
    public async Task HandleAsync_SlugTaken_IsAValidationErrorAtTheSlug()
    {
        var setup = new Setup();
        setup.Store.TakenSlugs.Add("acme");

        var error = await Assert.ThrowsAsync<DomainValidationException>(() => setup.Handler.HandleAsync(Command(), TestContext.Current.CancellationToken));

        Assert.Equal(("slug", "slug-taken"), (error.Field, error.Code));
    }

    [Fact]
    public async Task HandleAsync_PasswordTooShort_IsAValidationErrorAtThePasswordAndStoresNothing()
    {
        var setup = new Setup();

        var error = await Assert.ThrowsAsync<DomainValidationException>(() => setup.Handler.HandleAsync(Command(password: "short"), TestContext.Current.CancellationToken));

        Assert.Equal(("password", "password-too-short"), (error.Field, error.Code));
        Assert.Empty(setup.Store.Saved);
    }

    [Fact]
    public async Task HandleAsync_WithEmailSettings_StoresThemWithTheSmtpPasswordProtected()
    {
        var setup = new Setup();
        var smtp = new Askwerk.Application.Email.SmtpSettings("smtp.acme.at", 587, "surveys@acme.at", "smtp-secret", "Acme Surveys <surveys@acme.at>");

        await setup.Handler.HandleAsync(Command() with { Email = smtp }, TestContext.Current.CancellationToken);

        var settings = setup.Store.SavedOf<InstanceSettings>().Single();
        Assert.Equal(("smtp.acme.at", 587, "surveys@acme.at", "protected:smtp-secret", "Acme Surveys <surveys@acme.at>"), (settings.SmtpHost, settings.SmtpPort, settings.SmtpUserName, settings.SmtpPassword, settings.SmtpSender));
    }
}
