// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Api.App.Results;
using Askwerk.Application.Auditing;
using Askwerk.Application.Exports;
using Askwerk.Application.Storage;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Integrations;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Exports;

/// <summary>Who asks for or downloads an export: a person in the designer, or an API key.</summary>
/// <param name="UserId">The person.</param>
/// <param name="ApiKeyId">The key.</param>
/// <param name="ClientId">An OAuth client (AWK-32), for the audit log.</param>
public sealed record ExportActor(Guid? UserId, Guid? ApiKeyId, Guid? ClientId = null)
{
    /// <summary>The audit log's view of the actor.</summary>
    internal (AuditActorKind Kind, string Id) Audit =>
        ClientId is { } client ? (AuditActorKind.OAuthClient, client.ToString()) : ApiKeyId is { } key ? (AuditActorKind.ApiKey, key.ToString()) : (AuditActorKind.User, UserId.ToString()!);
}

/// <summary>An export as the designer and the Public API show it, with what its job says.</summary>
/// <param name="Export">The export.</param>
/// <param name="Status">queued, running, ready, failed or expired.</param>
/// <param name="Error">Why it failed: a stable code.</param>
/// <param name="RequestedBy">The person's name or the key's name.</param>
/// <param name="ByApiKey">Whether a key asked for it.</param>
/// <param name="Attempt">The failed tries so far, while it will be tried again.</param>
/// <param name="RetryAt">When it is tried again (UTC).</param>
public sealed record ExportView(DataExport Export, string Status, string? Error, string RequestedBy, bool ByApiKey, int? Attempt, DateTimeOffset? RetryAt);

/// <summary>Why an export was refused: a stable code and its HTTP status.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Code">survey-not-found, unknown-format, not-published, too-many-exports, export-not-found, export-not-ready or export-expired.</param>
public sealed record ExportRefusal(int Status, string Code);

/// <summary>
/// Exports as jobs (AWK-25, W14): asked for here, prepared by <see cref="PrepareExportJob"/>, kept seven days and then
/// deleted by <see cref="DeleteExportJob"/>. The designer and the Public API share it; each checks its caller first.
/// </summary>
public sealed class ExportService(AskwerkDbContext context, ExportWriter writer, IFileStorage storage, IAuditWriter audit, TimeProvider time, ITraceContext trace)
{
    /// <summary>Exports of an organisation that may run at the same time (W14).</summary>
    public const int MaxRunning = 3;

    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    /// <summary>Asks for an export; the caller saves the unit of work.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">Format, layout, values, language and filter.</param>
    /// <param name="actor">Who asks.</param>
    /// <param name="idempotencyKey">A key's Idempotency-Key: the same one within 24 hours gives the same export.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The export, or why it was refused.</returns>
    public async Task<(DataExport? Export, ExportRefusal? Refusal)> StartAsync(Guid surveyId, ExportRequest request, ExportActor actor, string? idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        var survey = await context.Surveys.FirstOrDefaultAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken);
        var now = time.GetUtcNow();

        if (survey is null)
        {
            return (null, new ExportRefusal(StatusCodes.Status404NotFound, "survey-not-found"));
        }

        if (actor.ApiKeyId is { } apiKeyId && !string.IsNullOrWhiteSpace(idempotencyKey)
            && await context.DataExports.FirstOrDefaultAsync(row => row.RequestedByApiKeyId == apiKeyId && row.IdempotencyKey == idempotencyKey && row.CreatedAt > now - IdempotencyWindow, cancellationToken) is { } earlier)
        {
            return (earlier, null);
        }

        if (writer.ExporterOf(request.Format) is not { } exporter)
        {
            return (null, new ExportRefusal(StatusCodes.Status400BadRequest, "unknown-format"));
        }

        if (ExportWriter.Shown(await writer.PublishedVersionsAsync(surveyId, cancellationToken), request.Version) is null)
        {
            return (null, new ExportRefusal(StatusCodes.Status409Conflict, "not-published"));
        }

        var running = await context.DataExports
            .Where(row => row.Status == DataExportStatus.Queued || row.Status == DataExportStatus.Running)
            .CountAsync(row => context.Jobs.Any(job => job.Id == row.JobId && job.DoneAt == null), cancellationToken);

        if (running >= MaxRunning)
        {
            return (null, new ExportRefusal(StatusCodes.Status429TooManyRequests, "too-many-exports"));
        }

        var exportId = Guid.CreateVersion7(now);
        var jobId = JobRunner.Enqueue(context, survey.TenantId, PrepareExportJob.JobKind, new ExportJobPayload(exportId), now, now);
        var options = JsonSerializer.Serialize(request with { Format = exporter.Format.ToString().ToLowerInvariant() }, JsonSerializerOptions.Web);
        var export = DataExport.Request(exportId, survey.TenantId, surveyId, exporter.Format.ToString().ToLowerInvariant(), options, actor.UserId, actor.ApiKeyId, idempotencyKey?.Trim(), jobId, now);
        context.DataExports.Add(export);

        return (export, null);
    }

    /// <summary>One export of the organisation.</summary>
    /// <param name="exportId">The export.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The export, or null.</returns>
    public async Task<ExportView?> FindAsync(Guid exportId, CancellationToken cancellationToken) =>
        (await ViewsAsync(context.DataExports.Where(row => row.Id == exportId), cancellationToken)).FirstOrDefault();

    /// <summary>A survey's exports of the last seven days, newest first (W14 A4).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The exports.</returns>
    public Task<List<ExportView>> RecentAsync(Guid surveyId, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - DataExport.KeptFor;

        return ViewsAsync(context.DataExports.Where(row => row.SurveyId == surveyId && row.CreatedAt > since).OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id), cancellationToken);
    }

    /// <summary>Opens an export's file and audits the download; the caller saves the unit of work.</summary>
    /// <param name="exportId">The export.</param>
    /// <param name="actor">Who downloads.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The file, or why it cannot be had.</returns>
    public async Task<(Stream? File, DataExport? Export, ExportRefusal? Refusal)> OpenAsync(Guid exportId, ExportActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (await FindAsync(exportId, cancellationToken) is not { } view)
        {
            return (null, null, new ExportRefusal(StatusCodes.Status404NotFound, "export-not-found"));
        }

        if (view.Status == "expired")
        {
            return (null, null, new ExportRefusal(StatusCodes.Status410Gone, "export-expired"));
        }

        if (view.Status != "ready" || view.Export.FileKey is null || storage.OpenRead(view.Export.FileKey) is not { } file)
        {
            return (null, null, new ExportRefusal(StatusCodes.Status409Conflict, "export-not-ready"));
        }

        var (kind, id) = actor.Audit;
        await audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), view.Export.TenantId, kind, id, "export.downloaded", "Export", view.Export.Id.ToString(), view.Export.FileName, trace.CurrentTraceId),
            cancellationToken);

        return (file, view.Export, null);
    }

    private async Task<List<ExportView>> ViewsAsync(IQueryable<DataExport> exports, CancellationToken cancellationToken)
    {
        var rows = await exports
            .Select(row => new
            {
                Export = row,
                Job = context.Jobs.Where(job => job.Id == row.JobId).Select(job => new { job.DoneAt, job.Attempts, job.Error, job.RunAt }).FirstOrDefault(),
                User = context.Users.Where(user => user.Id == row.RequestedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
                Key = context.ApiKeys.Where(key => key.Id == row.RequestedByApiKeyId).Select(key => key.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row =>
        {
            var waiting = row.Export.Status is DataExportStatus.Queued or DataExportStatus.Running;
            var gaveUp = waiting && row.Job?.DoneAt is not null;
            var retrying = waiting && !gaveUp && row.Job is { Error: not null };

            return new ExportView(
                row.Export,
                gaveUp ? "failed" : row.Export.Status.ToString().ToLowerInvariant(),
                gaveUp ? "export-failed" : row.Export.Error,
                row.Key ?? row.User ?? "",
                row.Export.RequestedByApiKeyId is not null,
                retrying ? row.Job!.Attempts : null,
                retrying ? row.Job!.RunAt : null);
        })];
    }
}

/// <summary>Payload of the export jobs.</summary>
/// <param name="ExportId">The export.</param>
public sealed record ExportJobPayload(Guid ExportId);

/// <summary>
/// Prepares an export (AWK-25): writes the file while it reports progress, stores it with the organisation's files,
/// keeps it seven days, and audits the export with the person or key who asked. A failed try runs again like any
/// job; an export the format cannot hold fails at once.
/// </summary>
public sealed class PrepareExportJob(AskwerkDbContext context, ExportWriter writer, IFileStorage storage, IAuditWriter audit, TimeProvider time, ITraceContext trace) : IJobHandler
{
    /// <summary>The job kind.</summary>
    public const string JobKind = "prepare-export";

    /// <inheritdoc />
    public string Kind => JobKind;

    /// <inheritdoc />
    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var work = JsonSerializer.Deserialize<ExportJobPayload>(payload, JsonSerializerOptions.Web)!;
        var export = await context.DataExports.SingleAsync(row => row.Id == work.ExportId, cancellationToken);

        if (export.Status is not (DataExportStatus.Queued or DataExportStatus.Running))
        {
            return;
        }

        var survey = await context.Surveys.SingleAsync(row => row.Id == export.SurveyId, cancellationToken);
        var request = JsonSerializer.Deserialize<ExportRequest>(export.Options, JsonSerializerOptions.Web)!;
        var exporter = writer.ExporterOf(export.Format) ?? throw new InvalidOperationException($"Unknown export format {export.Format}.");
        export.Start(await writer.CountAsync(survey.Id, new ResultsFilter(request.Status, request.From, request.To, request.Version), cancellationToken));
        await context.SaveChangesAsync(cancellationToken);

        await using var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        string fileName;

        try
        {
            fileName = await writer.WriteAsync(survey, exporter, request, time.GetUtcNow(), file, async done =>
            {
                export.Progress(done);
                await context.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (ExportTooLargeException tooLarge)
        {
            export.Fail(tooLarge.Code, time.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        file.Position = 0;
        var key = $"{export.TenantId:N}/{export.Id:N}-export";
        await storage.SaveAsync(key, file, cancellationToken);
        var now = time.GetUtcNow();
        export.Ready(key, fileName, exporter.ContentType, file.Length, now);
        var baseAddress = (await context.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.BaseAddress ?? "";
        await Askwerk.Api.Integrations.WebhookService.RaiseAsync(
            context,
            export.TenantId,
            "export.completed",
            new { exportId = export.Id, surveyId = export.SurveyId, format = export.Format, file = new { name = fileName, size = file.Length, url = $"{baseAddress}/api/v1/exports/{export.Id}/file" } },
            now,
            cancellationToken,
            export.SurveyId);
        JobRunner.Enqueue(context, export.TenantId, DeleteExportJob.JobKind, new ExportJobPayload(export.Id), export.ExpiresAt!.Value, now);
        var (kind, id) = new ExportActor(export.RequestedByUserId, export.RequestedByApiKeyId).Audit;
        var details = JsonSerializer.Serialize(new { format = export.Format, options = JsonDocument.Parse(export.Options).RootElement, export.Total });
        await audit.WriteAsync(new AuditContent(now, export.TenantId, kind, id, "results.exported", "Survey", survey.Id.ToString(), details, trace.CurrentTraceId), cancellationToken);
    }
}

/// <summary>Deletes an export's file once its seven days are over.</summary>
public sealed class DeleteExportJob(AskwerkDbContext context, IFileStorage storage) : IJobHandler
{
    /// <summary>The job kind.</summary>
    public const string JobKind = "delete-export";

    /// <inheritdoc />
    public string Kind => JobKind;

    /// <inheritdoc />
    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var work = JsonSerializer.Deserialize<ExportJobPayload>(payload, JsonSerializerOptions.Web)!;
        var export = await context.DataExports.SingleAsync(row => row.Id == work.ExportId, cancellationToken);

        if (export.FileKey is { } key)
        {
            storage.Delete(key);
        }

        export.Expire();
    }
}
