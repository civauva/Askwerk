// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Application.Email;

/// <summary>How Askwerk reaches its SMTP server (setup step 4, later Settings). Optional: without it, email features are off.</summary>
/// <param name="Host">SMTP host.</param>
/// <param name="Port">Port, usually 587 (STARTTLS) or 465.</param>
/// <param name="UserName">Login, if the server needs one.</param>
/// <param name="Password">Password, if the server needs one; encrypted at rest.</param>
/// <param name="Sender">From address, e.g. <c>Acme Surveys &lt;surveys@acme.at&gt;</c>.</param>
public sealed record SmtpSettings(string Host, int Port, string? UserName, string? Password, string Sender);

/// <summary>One email: plain text, and optionally an HTML part that mail clients show instead (AWK-17).</summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject.</param>
/// <param name="Body">Plain-text body.</param>
/// <param name="Html">HTML body, if any.</param>
public sealed record EmailMessage(string To, string Subject, string Body, string? Html = null);

/// <summary>Sends email through an SMTP server; the edge that tests replace with a recording fake.</summary>
public interface IEmailSender
{
    /// <summary>Sends one message.</summary>
    /// <param name="smtp">The server to use.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels sending.</param>
    /// <returns>A task that completes when the server accepted the message.</returns>
    Task SendAsync(SmtpSettings smtp, EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>The mail server refused the recipient (AWK-7, bounces): the address is not tried again.</summary>
/// <param name="message">What the server said.</param>
public sealed class EmailRejectedException(string message) : Exception(message);
