// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Mail;
using Askwerk.Application.Email;
using Askwerk.Application.Setup;
using Microsoft.AspNetCore.DataProtection;

namespace Askwerk.Api.Identity;

/// <summary>Encrypts stored secrets with the instance's data-protection keys.</summary>
public sealed class DataProtectionSecrets(IDataProtectionProvider protection) : ISecretProtector
{
    private readonly IDataProtector _protector = protection.CreateProtector("askwerk.secrets.v1");

    /// <inheritdoc />
    public string Protect(string secret) => _protector.Protect(secret);

    /// <inheritdoc />
    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>Sends through the SMTP server with .NET's built-in client (no extra dependency); STARTTLS is required.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(SmtpSettings smtp, EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(smtp);
        ArgumentNullException.ThrowIfNull(message);

        using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = true };

        if (!string.IsNullOrEmpty(smtp.UserName))
        {
            client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password);
        }

        using var mail = new MailMessage(MailAddress(smtp.Sender), new MailAddress(message.To)) { Subject = message.Subject, Body = message.Body };

        // Plain text first, HTML as the alternative mail clients prefer when they can show it.
        if (message.Html is { } html)
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, System.Text.Encoding.UTF8, "text/html"));
        }

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (SmtpFailedRecipientException exception) when (exception.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed or SmtpStatusCode.UserNotLocalTryAlternatePath or SmtpStatusCode.ExceededStorageAllocation)
        {
            throw new EmailRejectedException(exception.Message);
        }
    }

    private static MailAddress MailAddress(string sender) => new(sender);
}

/// <summary>The instance's SMTP settings, with the password decrypted; null while email is not set up.</summary>
public sealed class EmailSettingsProvider(IInstanceSettingsRepository settings, ISecretProtector secrets)
{
    /// <summary>Reads the settings.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The SMTP settings and the instance's base address and language, or null.</returns>
    public async Task<(SmtpSettings Smtp, string BaseAddress, string Language)?> FindAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.FindAsync(cancellationToken);

        if (stored?.SmtpHost is null || stored.SmtpSender is null)
        {
            return null;
        }

        var password = stored.SmtpPassword is null ? null : secrets.Unprotect(stored.SmtpPassword);

        return (new SmtpSettings(stored.SmtpHost, stored.SmtpPort ?? 587, stored.SmtpUserName, password, stored.SmtpSender), stored.BaseAddress, stored.DefaultLanguage);
    }
}

/// <summary>Short, factual account emails in German and English (ui-design-system.md, voice).</summary>
public static class EmailTexts
{
    /// <summary>The test email of setup step 4.</summary>
    /// <param name="to">Recipient.</param>
    /// <param name="language">de or en.</param>
    /// <returns>The message.</returns>
    public static EmailMessage Test(string to, string language) => German(language)
        ? new(to, "Askwerk: Test-E-Mail", "Diese E-Mail kommt von Askwerk. Der E-Mail-Versand funktioniert.")
        : new(to, "Askwerk: Test email", "This email comes from Askwerk. Sending email works.");

    /// <summary>The password reset link.</summary>
    /// <param name="to">Recipient.</param>
    /// <param name="link">The reset link.</param>
    /// <param name="language">de or en.</param>
    /// <returns>The message.</returns>
    public static EmailMessage PasswordReset(string to, string link, string language) => German(language)
        ? new(to, "Askwerk: Passwort zurücksetzen", $"Mit diesem Link setzen Sie ein neues Passwort. Er funktioniert einmal, 30 Minuten lang:\n\n{link}\n\nWenn Sie das nicht angefordert haben, ignorieren Sie diese E-Mail.")
        : new(to, "Askwerk: Reset your password", $"Use this link to set a new password. It works once, for 30 minutes:\n\n{link}\n\nIf you did not ask for this, ignore this email.");

    /// <summary>The notice after a lockout.</summary>
    /// <param name="to">Recipient.</param>
    /// <param name="until">When signing in is possible again.</param>
    /// <param name="language">de or en.</param>
    /// <returns>The message.</returns>
    public static EmailMessage Lockout(string to, DateTimeOffset until, string language) => German(language)
        ? new(to, "Askwerk: Anmeldung pausiert", $"Nach 5 falschen Passwörtern ist die Anmeldung für Ihr Konto bis {until:HH:mm} UTC pausiert. Wenn Sie das nicht waren, setzen Sie Ihr Passwort zurück.")
        : new(to, "Askwerk: Sign-in paused", $"After 5 wrong passwords, signing in to your account is paused until {until:HH:mm} UTC. If this was not you, reset your password.");

    /// <summary>An invitation into an organisation (AWK-30).</summary>
    /// <param name="to">Recipient.</param>
    /// <param name="organisation">The organisation's name.</param>
    /// <param name="invitedBy">Who invites.</param>
    /// <param name="link">The link to join; it works once, for 7 days.</param>
    /// <param name="language">de or en.</param>
    /// <returns>The message.</returns>
    public static EmailMessage MemberInvitation(string to, string organisation, string invitedBy, string link, string language) => German(language)
        ? new(to, $"Einladung zu {organisation} auf Askwerk", $"{invitedBy} lädt Sie zu {organisation} auf Askwerk ein.\n\nMit diesem Link treten Sie bei. Er funktioniert einmal, 7 Tage lang:\n\n{link}\n\nWenn Sie das nicht erwartet haben, ignorieren Sie diese E-Mail.")
        : new(to, $"Invitation to {organisation} on Askwerk", $"{invitedBy} invites you to {organisation} on Askwerk.\n\nUse this link to join. It works once, for 7 days:\n\n{link}\n\nIf you did not expect this, ignore this email.");

    private static bool German(string language) => language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
}
