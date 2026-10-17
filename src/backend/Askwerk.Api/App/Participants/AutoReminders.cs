// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Participants;

/// <summary>
/// Automatic reminders (W35): those who were invited and have not answered get a reminder a set number of days after
/// the invitation or their last reminder, up to a set number. One job per survey runs at 08:00 UTC each day while the
/// setting is on and plans its next run; a changed setting gets a new job, and the old one, seeing an older revision,
/// stops.
/// </summary>
public static class AutoReminders
{
    /// <summary>The job's kind.</summary>
    public const string JobKind = "auto-reminders";

    /// <summary>The setting and the next automatic reminder: when, and to how many.</summary>
    /// <param name="context">A context in the survey's organisation.</param>
    /// <param name="survey">The survey.</param>
    /// <param name="now">The time.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The settings.</returns>
    public static async Task<AutoReminderSettings> SettingsAsync(AskwerkDbContext context, Survey survey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(survey);

        if (!survey.AutoReminders)
        {
            return new AutoReminderSettings(false, survey.ReminderDays, survey.ReminderMax, null, 0);
        }

        var due = await DueAsync(context, survey, cancellationToken);
        var next = due.Count == 0 ? (DateTimeOffset?)null : NextRun(due.Min(person => person.DueAt), now);

        return new AutoReminderSettings(true, survey.ReminderDays, survey.ReminderMax, next, next is null ? 0 : due.Count(person => person.DueAt <= next));
    }

    /// <summary>Plans the job for the survey's current setting.</summary>
    public static void Plan(AskwerkDbContext context, Survey survey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(survey);

        if (survey.AutoReminders)
        {
            JobRunner.Enqueue(context, survey.TenantId, JobKind, new AutoRemindersPayload(survey.Id, survey.ReminderRevision), now, now);
        }
    }

    /// <summary>The people a reminder could still go to, and from when.</summary>
    internal static async Task<List<(Guid Id, DateTimeOffset DueAt)>> DueAsync(AskwerkDbContext context, Survey survey, CancellationToken cancellationToken)
    {
        var people = await context.Participants
            .Where(row => row.SurveyId == survey.Id && row.SentAt != null && row.BouncedAt == null && !row.RemindersOff && row.Reminders < survey.ReminderMax)
            .Where(row => !context.Responses.Any(response => response.ParticipantId == row.Id && response.Status == ResponseStatus.Complete))
            .Where(row => !context.Unsubscribes.Any(unsubscribe => unsubscribe.Email == row.Email))
            .Select(row => new { row.Id, Last = row.RemindedAt ?? row.SentAt!.Value })
            .ToListAsync(cancellationToken);

        return [.. people.Select(person => (person.Id, person.Last.AddDays(survey.ReminderDays)))];
    }

    /// <summary>The daily run at 08:00 UTC on or after the moment.</summary>
    internal static DateTimeOffset NextRun(DateTimeOffset at, DateTimeOffset now)
    {
        var from = at > now ? at : now;
        var day = new DateTimeOffset(from.UtcDateTime.Date.AddHours(8), TimeSpan.Zero);

        return day >= from ? day : day.AddDays(1);
    }
}

/// <summary>The automatic reminders of a survey (W35).</summary>
/// <param name="On">Whether they are on.</param>
/// <param name="Days">Days after the invitation or the last reminder.</param>
/// <param name="Max">Reminders per person at most.</param>
/// <param name="NextAt">The next automatic reminder (UTC), if anyone is due.</param>
/// <param name="NextCount">How many get it.</param>
public sealed record AutoReminderSettings(bool On, int Days, int Max, DateTimeOffset? NextAt, int NextCount);

/// <summary>Payload of the automatic reminders job.</summary>
/// <param name="SurveyId">The survey.</param>
/// <param name="Revision">The setting's revision it was planned for.</param>
public sealed record AutoRemindersPayload(Guid SurveyId, int Revision);

/// <summary>Sends the reminders that are due and plans the next run (W35).</summary>
public sealed class AutoRemindersJob(AskwerkDbContext context, TimeProvider time) : IJobHandler
{
    /// <inheritdoc />
    public string Kind => AutoReminders.JobKind;

    /// <inheritdoc />
    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var work = JsonSerializer.Deserialize<AutoRemindersPayload>(payload, JsonSerializerOptions.Web)!;
        var survey = await context.Surveys.FirstOrDefaultAsync(row => row.Id == work.SurveyId && row.DeletedAt == null, cancellationToken);
        var now = time.GetUtcNow();

        // Off, changed since, or no longer collecting: this chain ends.
        if (survey is null || !survey.AutoReminders || survey.ReminderRevision != work.Revision || !survey.IsCollecting(now))
        {
            return;
        }

        var due = await AutoReminders.DueAsync(context, survey, cancellationToken);
        var dueNow = due.Where(person => person.DueAt <= now).Select(person => person.Id).ToList();

        if (dueNow.Count > 0)
        {
            JobRunner.Enqueue(context, survey.TenantId, ParticipantsController.SendReminders, new SendRemindersPayload(survey.Id, InvitationId: null, dueNow, Automatic: true), now, now);
        }

        // Every day while it is on: new invitations bring new people.
        var later = due.Where(person => person.DueAt > now).Select(person => person.DueAt).DefaultIfEmpty(now.AddDays(1)).Min();
        JobRunner.Enqueue(context, survey.TenantId, AutoReminders.JobKind, work, AutoReminders.NextRun(later, now.AddMinutes(1)), now);
        await context.SaveChangesAsync(cancellationToken);
    }
}
