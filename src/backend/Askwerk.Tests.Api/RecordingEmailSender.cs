// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Email;

namespace Askwerk.Tests.Api;

/// <summary>Stands in for the SMTP server: keeps every message with the server it was sent through.</summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    public List<(SmtpSettings Smtp, EmailMessage Message)> Sent { get; } = [];

    /// <summary>Addresses the server refuses, like a mailbox that does not exist (AWK-7, bounces).</summary>
    public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(SmtpSettings smtp, EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailFor.Contains(message.To))
        {
            throw new EmailRejectedException($"Mailbox unavailable: {message.To}");
        }

        lock (Sent)
        {
            Sent.Add((smtp, message));
        }

        return Task.CompletedTask;
    }
}
