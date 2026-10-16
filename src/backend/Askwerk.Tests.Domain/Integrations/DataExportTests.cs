// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Integrations;

namespace Askwerk.Tests.Domain.Integrations;

public sealed class DataExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private static DataExport Requested(Guid? apiKey = null, string? idempotencyKey = null) =>
        DataExport.Request(
            Guid.CreateVersion7(Now),
            Guid.Parse("0193f2c1-0000-7000-8000-000000000001"),
            Guid.Parse("0193f2c1-0000-7000-8000-000000000003"),
            "csv",
            """{"layout":"wide"}""",
            apiKey is null ? Guid.Parse("0193f2c1-0000-7000-8000-000000000002") : null,
            apiKey,
            idempotencyKey,
            Guid.Parse("0193f2c1-0000-7000-8000-000000000009"),
            Now);

    [Fact]
    public void Request_IsQueued_ByAPersonOrAKey()
    {
        var byPerson = Requested();
        var byKey = Requested(apiKey: Guid.Parse("0193f2c1-0000-7000-8000-000000000004"), idempotencyKey: "2c7e");

        Assert.Equal(DataExportStatus.Queued, byPerson.Status);
        Assert.NotNull(byPerson.RequestedByUserId);
        Assert.Null(byPerson.RequestedByApiKeyId);
        Assert.Equal(("2c7e", (Guid?)null), (byKey.IdempotencyKey, byKey.RequestedByUserId));
        Assert.Equal(Guid.Parse("0193f2c1-0000-7000-8000-000000000009"), byPerson.JobId);
    }

    [Fact]
    public void Ready_KeepsTheFileSevenDays()
    {
        var export = Requested();
        export.Start(12400);
        export.Progress(3120);

        export.Ready("t/exports/e.csv", "customer-satisfaction-2026.csv", "text/csv", 1993421, Now.AddMinutes(1));

        Assert.Equal(DataExportStatus.Ready, export.Status);
        Assert.Equal((12400, 12400), (export.Done, export.Total));
        Assert.Equal(("customer-satisfaction-2026.csv", 1993421L), (export.FileName, export.Size));
        Assert.Equal(Now.AddMinutes(1).AddDays(7), export.ExpiresAt);
    }

    [Fact]
    public void Start_AgainAfterAFailedTry_BeginsFromNothing()
    {
        var export = Requested();
        export.Start(12400);
        export.Progress(3120);

        export.Start(12401);

        Assert.Equal((DataExportStatus.Running, 0, 12401), (export.Status, export.Done, export.Total));
    }

    [Fact]
    public void Fail_KeepsTheReason()
    {
        var export = Requested();

        export.Fail("too-many-rows", Now);

        Assert.Equal((DataExportStatus.Failed, "too-many-rows", (DateTimeOffset?)Now), (export.Status, export.Error, export.FinishedAt));
    }

    [Fact]
    public void Expire_ForgetsTheFile()
    {
        var export = Requested();
        export.Ready("t/exports/e.csv", "results.csv", "text/csv", 10, Now);

        export.Expire();

        Assert.Equal((DataExportStatus.Expired, (string?)null), (export.Status, export.FileKey));
    }
}
