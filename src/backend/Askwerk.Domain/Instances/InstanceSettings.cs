// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Instances;

/// <summary>What the first-run setup recorded about the installation (AWK-16, W2). Setup is complete once this exists.</summary>
/// <param name="InstanceId">The installation.</param>
/// <param name="Name">Instance name shown in the app and emails.</param>
/// <param name="BaseAddress">Base address for links until domains are set up (AWK-7).</param>
/// <param name="DefaultLanguage">Default language for new organisations and surveys.</param>
/// <param name="TimeZone">IANA time zone, e.g. Europe/Vienna.</param>
/// <param name="OwnerUserId">The instance owner created during setup.</param>
/// <param name="CompletedAt">When setup finished (UTC).</param>
/// <param name="SmtpHost">SMTP host; null when email is not set up (email features are then off).</param>
/// <param name="SmtpPort">SMTP port.</param>
/// <param name="SmtpUserName">SMTP login, if any.</param>
/// <param name="SmtpPassword">SMTP password, encrypted with the instance's data-protection keys.</param>
/// <param name="SmtpSender">From address.</param>
public sealed record InstanceSettings(
    Guid InstanceId,
    string Name,
    string BaseAddress,
    string DefaultLanguage,
    string TimeZone,
    Guid OwnerUserId,
    DateTimeOffset CompletedAt,
    string? SmtpHost = null,
    int? SmtpPort = null,
    string? SmtpUserName = null,
    string? SmtpPassword = null,
    string? SmtpSender = null);
