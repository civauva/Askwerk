// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Api.App.Messages;
using Askwerk.Api.App.Surveys;
using Askwerk.Api.Identity;
using Askwerk.Application.Email;
using Askwerk.Domain.Messages;
using Askwerk.Domain.Participants;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Participants;

/// <summary>
/// Sends invitations and reminders (AWK-7): each person gets the organisation's text in their language, their own
/// link and a link to unsubscribe. Progress is saved after every email, so a retry sends only what is left. A refused
/// address is marked bounced and not tried again; unsubscribed people get nothing.
/// </summary>
public abstract class InvitationJob(AskwerkDbContext context, EmailSettingsProvider email, IEmailSender sender, TimeProvider time) : IJobHandler
{
    /// <inheritdoc />
    public abstract string Kind { get; }

    /// <inheritdoc />
    public abstract Task RunAsync(string payload, CancellationToken cancellationToken);

    /// <summary>Sends one kind of message to the people; marks each with the result.</summary>
    protected async Task SendAsync(MessageKind kind, Survey survey, IReadOnlyList<Participant> people, IReadOnlyDictionary<Guid, Invitation> invitations, Action<Participant> sent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(invitations);
        ArgumentNullException.ThrowIfNull(sent);

        if (people.Count == 0)
        {
            return;
        }

        // Without email the job waits and tries again; nothing is lost.
        var (smtp, baseAddress, _) = await email.FindAsync(cancellationToken) ?? throw new InvalidOperationException("Email is not set up.");
        var links = await SurveyLinks.OfAsync(context, baseAddress, survey, cancellationToken);
        var unsubscribed = (await context.Unsubscribes.Select(row => row.Email).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        foreach (var person in people.Where(person => person.BouncedAt is null && !unsubscribed.Contains(person.Email)))
        {
            var invitation = invitations[person.InvitationId];
            var link = ParticipantsController.PersonalLink(links, invitation.Host, person.Token);
            var message = await MessageComposer.ComposeAsync(
                context,
                baseAddress.TrimEnd('/'),
                new MessageFor(kind, survey, person.Language, person.Name ?? "", link, invitation.Deadline, $"{new Uri(link).GetLeftPart(UriPartial.Authority)}/s/u/{person.Token}", Fields: PersonDetails.ReadFields(person.Fields), ExternalId: person.ExternalId),
                cancellationToken);

            try
            {
                await sender.SendAsync(smtp, new EmailMessage(person.Email, message.Subject, message.Text, message.Html), cancellationToken);
                sent(person);
            }
            catch (EmailRejectedException)
            {
                person.Bounced(time.GetUtcNow());
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Sends an invitation's emails.</summary>
public sealed class SendInvitationsJob(AskwerkDbContext context, EmailSettingsProvider email, IEmailSender sender, TimeProvider time) : InvitationJob(context, email, sender, time)
{
    private readonly AskwerkDbContext context = context;
    private readonly TimeProvider time = time;

    /// <inheritdoc />
    public override string Kind => ParticipantsController.SendInvitations;

    /// <inheritdoc />
    public override async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var work = JsonSerializer.Deserialize<SendInvitationsPayload>(payload, JsonSerializerOptions.Web)!;

        // Again to the chosen people (W35), whichever invitation they came with.
        if (work.ParticipantIds is { Count: > 0 } chosen)
        {
            var again = await context.Participants.Where(row => chosen.Contains(row.Id) && row.BouncedAt == null).OrderBy(row => row.Email).ToListAsync(cancellationToken);
            var surveyIds = again.Select(row => row.SurveyId).Distinct().ToList();
            var first = await context.Surveys.SingleAsync(row => row.Id == surveyIds.Single(), cancellationToken);
            var theirs = await context.Invitations.Where(row => row.SurveyId == first.Id).ToDictionaryAsync(row => row.Id, cancellationToken);

            await SendAsync(MessageKind.Invitation, first, again, theirs, person => person.Sent(time.GetUtcNow()), cancellationToken);
            return;
        }

        var invitation = await context.Invitations.SingleAsync(row => row.Id == work.InvitationId, cancellationToken);
        var survey = await context.Surveys.SingleAsync(row => row.Id == invitation.SurveyId, cancellationToken);
        var people = await context.Participants.Where(row => row.InvitationId == invitation.Id && row.SentAt == null && row.BouncedAt == null).OrderBy(row => row.Email).ToListAsync(cancellationToken);

        await SendAsync(MessageKind.Invitation, survey, people, new Dictionary<Guid, Invitation> { [invitation.Id] = invitation }, person => person.Sent(time.GetUtcNow()), cancellationToken);
    }
}

/// <summary>Reminds those who were invited and have not answered.</summary>
public sealed class SendRemindersJob(AskwerkDbContext context, EmailSettingsProvider email, IEmailSender sender, TimeProvider time) : InvitationJob(context, email, sender, time)
{
    private readonly AskwerkDbContext context = context;
    private readonly TimeProvider time = time;

    /// <inheritdoc />
    public override string Kind => ParticipantsController.SendReminders;

    /// <inheritdoc />
    public override async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var work = JsonSerializer.Deserialize<SendRemindersPayload>(payload, JsonSerializerOptions.Web)!;
        var survey = await context.Surveys.SingleAsync(row => row.Id == work.SurveyId, cancellationToken);
        var answered = await context.Responses.Where(row => row.SurveyId == survey.Id && row.ParticipantId != null && row.Status == ResponseStatus.Complete).Select(row => row.ParticipantId!.Value).ToListAsync(cancellationToken);
        var people = await context.Participants
            .Where(row => row.SurveyId == survey.Id && row.SentAt != null && row.BouncedAt == null && !answered.Contains(row.Id))
            .Where(row => work.InvitationId == null || row.InvitationId == work.InvitationId)
            .Where(row => work.ParticipantIds == null || work.ParticipantIds.Contains(row.Id))
            .OrderBy(row => row.Email)
            .ToListAsync(cancellationToken);
        var invitations = await context.Invitations.Where(row => row.SurveyId == survey.Id).ToDictionaryAsync(row => row.Id, cancellationToken);

        await SendAsync(MessageKind.Reminder, survey, people, invitations, person => person.Reminded(time.GetUtcNow()), cancellationToken);
    }
}
