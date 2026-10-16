// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using Askwerk.Application.Setup;
using Askwerk.Domain.Instances;
using Askwerk.Infrastructure.Identity;

namespace Askwerk.Api.Configuration;

/// <summary>
/// Installation settings fixed by whoever runs the server (AWK-39, W30). They win over what administrators save in the
/// app, which shows them as locked. Email is one group: any ASKWERK_SMTP_* value locks all of it.
/// </summary>
/// <param name="Name">ASKWERK_NAME.</param>
/// <param name="DefaultLanguage">ASKWERK_LANGUAGE: de or en.</param>
/// <param name="TimeZone">ASKWERK_TIME_ZONE: an IANA time zone.</param>
/// <param name="SmtpHost">ASKWERK_SMTP_HOST.</param>
/// <param name="SmtpPort">ASKWERK_SMTP_PORT.</param>
/// <param name="SmtpUserName">ASKWERK_SMTP_USER.</param>
/// <param name="SmtpPassword">ASKWERK_SMTP_PASSWORD, in plain text.</param>
/// <param name="SmtpSender">ASKWERK_SMTP_SENDER.</param>
public sealed record InstallationOverrides(
    string? Name = null,
    string? DefaultLanguage = null,
    string? TimeZone = null,
    string? SmtpHost = null,
    int? SmtpPort = null,
    string? SmtpUserName = null,
    string? SmtpPassword = null,
    string? SmtpSender = null)
{
    /// <summary>Gets whether any email value comes from the configuration.</summary>
    public bool LocksEmail => SmtpHost is not null || SmtpPort is not null || SmtpUserName is not null || SmtpPassword is not null || SmtpSender is not null;

    /// <summary>Gets the settings the app cannot change: the address always, and whatever the configuration sets.</summary>
    public IReadOnlyList<string> Locked =>
    [
        "baseAddress",
        .. Name is null ? Array.Empty<string>() : ["name"],
        .. DefaultLanguage is null ? Array.Empty<string>() : ["defaultLanguage"],
        .. TimeZone is null ? Array.Empty<string>() : ["timeZone"],
        .. LocksEmail ? ["email"] : Array.Empty<string>(),
    ];

    /// <summary>Reads the values from configuration (environment variables in the container).</summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The overrides; empty when none is set.</returns>
    public static InstallationOverrides From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? Value(string key) => string.IsNullOrWhiteSpace(configuration[key]) ? null : configuration[key]!.Trim();

        return new InstallationOverrides(
            Value("ASKWERK_NAME"),
            Value("ASKWERK_LANGUAGE"),
            Value("ASKWERK_TIME_ZONE"),
            Value("ASKWERK_SMTP_HOST"),
            int.TryParse(Value("ASKWERK_SMTP_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : null,
            Value("ASKWERK_SMTP_USER"),
            Value("ASKWERK_SMTP_PASSWORD"),
            Value("ASKWERK_SMTP_SENDER"));
    }

    /// <summary>The stored settings with the configured values over them.</summary>
    /// <param name="stored">What the app saved.</param>
    /// <param name="secrets">Encrypts a configured password the way stored ones are.</param>
    /// <returns>The settings in effect.</returns>
    public InstanceSettings Apply(InstanceSettings stored, ISecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(secrets);

        var settings = stored with
        {
            Name = Name ?? stored.Name,
            DefaultLanguage = DefaultLanguage ?? stored.DefaultLanguage,
            TimeZone = TimeZone ?? stored.TimeZone,
        };

        return LocksEmail
            ? settings with
            {
                SmtpHost = SmtpHost,
                SmtpPort = SmtpPort,
                SmtpUserName = SmtpUserName,
                SmtpPassword = SmtpPassword is null ? null : secrets.Protect(SmtpPassword),
                SmtpSender = SmtpSender,
            }
            : settings;
    }
}

/// <summary>The saved installation settings with <see cref="InstallationOverrides"/> over them (AWK-39).</summary>
public sealed class ConfiguredInstanceSettingsRepository(EfInstanceSettingsRepository stored, InstallationOverrides overrides, ISecretProtector secrets) : IInstanceSettingsRepository
{
    /// <inheritdoc />
    public async Task<InstanceSettings?> FindAsync(CancellationToken cancellationToken) =>
        await stored.FindAsync(cancellationToken) is { } settings ? overrides.Apply(settings, secrets) : null;

    /// <inheritdoc />
    public void Add(InstanceSettings settings) => stored.Add(settings);
}
