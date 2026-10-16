// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Integrations;

/// <summary>Where an export stands.</summary>
public enum DataExportStatus
{
    /// <summary>Waiting for its job.</summary>
    Queued,

    /// <summary>Being written.</summary>
    Running,

    /// <summary>The file can be downloaded.</summary>
    Ready,

    /// <summary>It could not be prepared.</summary>
    Failed,

    /// <summary>The file was deleted.</summary>
    Expired,
}

/// <summary>
/// A results export prepared by a job (AWK-25, W14 A), for the designer and the Public API alike: its options,
/// its progress, and its file, which is kept seven days.
/// </summary>
public sealed class DataExport
{
    /// <summary>How long a ready file is kept (owner decision, W14).</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(7);

    private DataExport()
    {
        Format = "";
        Options = "{}";
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The survey.</summary>
    public Guid SurveyId { get; private set; }

    /// <summary>excel, csv, spss, stata or json.</summary>
    public string Format { get; private set; }

    /// <summary>Layout, values, language and filter as JSON.</summary>
    public string Options { get; private set; }

    /// <summary>Where it stands.</summary>
    public DataExportStatus Status { get; private set; }

    /// <summary>The person who asked.</summary>
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>The key that asked.</summary>
    public Guid? RequestedByApiKeyId { get; private set; }

    /// <summary>The caller's Idempotency-Key.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The job that prepares it.</summary>
    public Guid JobId { get; private set; }

    /// <summary>Responses written.</summary>
    public int Done { get; private set; }

    /// <summary>Responses to write.</summary>
    public int Total { get; private set; }

    /// <summary>The file's storage key.</summary>
    public string? FileKey { get; private set; }

    /// <summary>The file's name.</summary>
    public string? FileName { get; private set; }

    /// <summary>The file's media type.</summary>
    public string? ContentType { get; private set; }

    /// <summary>The file's size in bytes.</summary>
    public long? Size { get; private set; }

    /// <summary>Why it failed.</summary>
    public string? Error { get; private set; }

    /// <summary>Asked for (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Ready or failed (UTC).</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>When the file is deleted (UTC).</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>A new export, queued.</summary>
    /// <param name="id">Stable id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="surveyId">The survey.</param>
    /// <param name="format">The format's name.</param>
    /// <param name="options">The other choices as JSON.</param>
    /// <param name="userId">The person who asked, or null.</param>
    /// <param name="apiKeyId">The key that asked, or null.</param>
    /// <param name="idempotencyKey">The caller's Idempotency-Key, if any.</param>
    /// <param name="jobId">The job that prepares it.</param>
    /// <param name="now">Now.</param>
    /// <returns>The export.</returns>
    public static DataExport Request(Guid id, Guid tenantId, Guid surveyId, string format, string options, Guid? userId, Guid? apiKeyId, string? idempotencyKey, Guid jobId, DateTimeOffset now) => new()
    {
        Id = id,
        TenantId = tenantId,
        SurveyId = surveyId,
        Format = format,
        Options = options,
        Status = DataExportStatus.Queued,
        RequestedByUserId = userId,
        RequestedByApiKeyId = apiKeyId,
        IdempotencyKey = idempotencyKey,
        JobId = jobId,
        CreatedAt = now,
    };

    /// <summary>A try begins; a later try starts from nothing.</summary>
    /// <param name="total">Responses to write.</param>
    public void Start(int total)
    {
        Status = DataExportStatus.Running;
        Total = total;
        Done = 0;
        Error = null;
    }

    /// <summary>Responses written so far.</summary>
    /// <param name="done">How many.</param>
    public void Progress(int done) => Done = done;

    /// <summary>The file is stored and kept for <see cref="KeptFor"/>.</summary>
    /// <param name="fileKey">Its storage key.</param>
    /// <param name="fileName">Its name.</param>
    /// <param name="contentType">Its media type.</param>
    /// <param name="size">Its size in bytes.</param>
    /// <param name="now">Now.</param>
    public void Ready(string fileKey, string fileName, string contentType, long size, DateTimeOffset now)
    {
        Status = DataExportStatus.Ready;
        Done = Total;
        FileKey = fileKey;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        Error = null;
        FinishedAt = now;
        ExpiresAt = now + KeptFor;
    }

    /// <summary>It cannot be prepared.</summary>
    /// <param name="error">A stable code, e.g. too-many-rows.</param>
    /// <param name="now">Now.</param>
    public void Fail(string error, DateTimeOffset now)
    {
        Status = DataExportStatus.Failed;
        Error = error;
        FinishedAt = now;
    }

    /// <summary>The file is deleted.</summary>
    public void Expire()
    {
        Status = DataExportStatus.Expired;
        FileKey = null;
    }
}
