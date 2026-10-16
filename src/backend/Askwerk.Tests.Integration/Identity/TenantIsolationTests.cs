// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Askwerk.Tests.Integration.Identity;

/// <summary>Row-level security keeps tenants apart in the database itself, whatever the code above it does.</summary>
public sealed class TenantIsolationTests(PostgresServer server)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 21, 0, 0, TimeSpan.Zero);
    private static readonly Guid AcmeId = Guid.Parse("0199e000-0000-7000-8000-00000000000a");
    private static readonly Guid OtherId = Guid.Parse("0199e000-0000-7000-8000-00000000000b");
    private static readonly Guid AnnaId = Guid.Parse("0199e000-0000-7000-8000-0000000000a1");
    private static readonly Guid BenId = Guid.Parse("0199e000-0000-7000-8000-0000000000b1");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_InsertedForTheTenantInContext_IsStored()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, Cancel);
        var session = new DbSessionContext { TenantId = AcmeId };

        await using (var context = database.CreateAppContext(session))
        {
            context.Tenants.Add(Tenant.Create(AcmeId, "Acme GmbH", "acme", Now));
            await context.SaveChangesAsync(Cancel);
        }

        await using var check = AskwerkDbContext.Create(database.OwnerConnectionString);
        Assert.Equal("acme", (await check.Tenants.SingleAsync(Cancel)).Slug);
    }

    [Fact]
    public async Task Tenant_InsertedWithoutTenantContext_IsRejectedByTheDatabase()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, Cancel);
        await using var context = database.CreateAppContext(new DbSessionContext());
        context.Tenants.Add(Tenant.Create(AcmeId, "Acme GmbH", "acme", Now));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Cancel));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Memberships_InATenantContext_ShowOnlyThatTenant()
    {
        var database = await SeededAsync();

        await using var context = database.CreateAppContext(new DbSessionContext { TenantId = AcmeId });
        var memberships = await context.Memberships.ToListAsync(Cancel);

        Assert.Equal([AnnaId], memberships.Select(membership => membership.UserId));
    }

    [Fact]
    public async Task TenantsAndMemberships_ForASignedInUserWithoutTenant_ShowOnlyTheirOwn()
    {
        var database = await SeededAsync();

        await using var context = database.CreateAppContext(new DbSessionContext { UserId = BenId });
        var tenants = await context.Tenants.Select(tenant => tenant.Slug).ToListAsync(Cancel);
        var memberships = await context.Memberships.Select(membership => membership.TenantId).ToListAsync(Cancel);

        Assert.Equal(["other"], tenants);
        Assert.Equal([OtherId], memberships);
    }

    [Fact]
    public async Task Tenants_WithoutAnyContext_ShowNothing()
    {
        var database = await SeededAsync();

        await using var context = database.CreateAppContext(new DbSessionContext());
        var count = await context.Tenants.CountAsync(Cancel);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Tenant_SameSlugTwice_IsRejected()
    {
        var database = await SeededAsync();
        await using var context = AskwerkDbContext.Create(database.OwnerConnectionString);
        context.Tenants.Add(Tenant.Create(Guid.CreateVersion7(Now), "Acme AG", "acme", Now));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Cancel));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task User_SameEmailInOtherCase_IsRejected()
    {
        var database = await SeededAsync();
        await using var context = AskwerkDbContext.Create(database.OwnerConnectionString);
        context.Users.Add(User.Create(Guid.CreateVersion7(Now), "ANNA@acme.at", "Anna B.", Now));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Cancel));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    /// <summary>Acme with Anna as owner, Other with Ben as editor; seeded as the database owner, which RLS does not restrict.</summary>
    private async Task<TestDatabase> SeededAsync()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, Cancel);
        await using var context = AskwerkDbContext.Create(database.OwnerConnectionString);
        context.Tenants.AddRange(Tenant.Create(AcmeId, "Acme GmbH", "acme", Now), Tenant.Create(OtherId, "Other AG", "other", Now));
        context.Users.AddRange(User.Create(AnnaId, "anna@acme.at", "Anna Berger", Now), User.Create(BenId, "ben@other.ch", "Ben Keller", Now));
        context.Memberships.AddRange(
            new Membership(Guid.CreateVersion7(Now), AnnaId, AcmeId, null, TenantRole.Owner, Now),
            new Membership(Guid.CreateVersion7(Now), BenId, OtherId, null, TenantRole.Editor, Now));
        await context.SaveChangesAsync(Cancel);

        return database;
    }
}
