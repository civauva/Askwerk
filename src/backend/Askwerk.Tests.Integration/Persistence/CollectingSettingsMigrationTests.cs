// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Askwerk.Tests.Integration.Persistence;

/// <summary>The CollectingSettings migration (AWK-29, W24) against the previous schema with a survey in it.</summary>
public sealed class CollectingSettingsMigrationTests(PostgresServer server)
{
    private const string Previous = "20261015110822_LdapSignIn";
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_OnAnExistingSurvey_KeepsTodaysBehaviour()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, Cancel);
        var tenantId = Guid.CreateVersion7(Now);
        var userId = Guid.CreateVersion7(Now);
        var surveyId = Guid.CreateVersion7(Now);

        await using (var context = AskwerkDbContext.Create(database.OwnerConnectionString))
        {
            context.Tenants.Add(Tenant.Create(tenantId, "Acme GmbH", "acme", Now));
            context.Users.Add(User.Create(userId, "anna@acme.at", "Anna Berger", Now));
            var (survey, draft) = Survey.Create(surveyId, tenantId, "Customer satisfaction 2026", ["de"], "de", userId, Now);
            context.Surveys.Add(survey);
            context.SurveyVersions.Add(draft);
            await context.SaveChangesAsync(Cancel);
            await context.GetService<IMigrator>().MigrateAsync(Previous, cancellationToken: Cancel);
        }

        await using (var context = AskwerkDbContext.Create(database.OwnerConnectionString))
        {
            await context.Database.MigrateAsync(Cancel);
        }

        await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = new NpgsqlCommand($"SELECT one_response_per_person, closes_at IS NULL FROM survey WHERE id = '{surveyId}'", connection);
        await using var reader = await command.ExecuteReaderAsync(Cancel);
        await reader.ReadAsync(Cancel);

        Assert.Equal((true, true), (reader.GetBoolean(0), reader.GetBoolean(1)));
    }
}
