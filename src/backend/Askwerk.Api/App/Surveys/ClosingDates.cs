// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Api.Integrations;
using Askwerk.Application.Auditing;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Surveys;

/// <summary>Closing dates (AWK-29, W24): entered as a local date and time in the instance's time zone, stored in UTC.</summary>
public static class ClosingDates
{
    /// <summary>The moment a local date and time names in a time zone.</summary>
    /// <param name="local">yyyy-MM-ddTHH:mm, as the browser's date-time field gives it.</param>
    /// <param name="timeZone">An IANA time zone, e.g. Europe/Vienna.</param>
    /// <returns>The moment in UTC; null when the text is no date and time or names an hour the clocks skip.</returns>
    public static DateTimeOffset? ToUtc(string local, string timeZone)
    {
        if (!DateTime.TryParseExact(local, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            || zone.IsInvalidTime(parsed))
        {
            return null;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(parsed, zone), TimeSpan.Zero);
    }
}

/// <summary>
/// Closes a survey once its closing date has passed (W24), as the system: audit entry survey.closed with the reason
/// closing-date, and the survey.closed webhook. A date moved later leaves this job with nothing to do; the new date has
/// its own job.
/// </summary>
public sealed class CloseSurveyJob(AskwerkDbContext context, IAuditWriter audit, TimeProvider time, ITraceContext trace) : IJobHandler
{
    /// <summary>The job's kind.</summary>
    public const string JobKind = "close-survey";

    /// <inheritdoc />
    public string Kind => JobKind;

    /// <inheritdoc />
    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var surveyId = JsonDocument.Parse(payload).RootElement.GetProperty("surveyId").GetGuid();
        var survey = await context.Surveys.FirstOrDefaultAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken);
        var now = time.GetUtcNow();

        if (survey is null || !survey.CloseAtClosingDate(now))
        {
            return;
        }

        await audit.WriteAsync(
            new AuditContent(now, survey.TenantId, AuditActorKind.System, null, "survey.closed", "Survey", survey.Id.ToString(), JsonSerializer.Serialize(new { reason = "closing-date", closesAt = survey.ClosesAt }), trace.CurrentTraceId),
            cancellationToken);
        await WebhookService.RaiseAsync(context, survey.TenantId, "survey.closed", new { surveyId = survey.Id }, now, cancellationToken, survey.Id);
    }
}
