// SPDX-License-Identifier: AGPL-3.0-only
using System.Reflection;

namespace Askwerk.Api.Configuration;

/// <summary>
/// The few values Askwerk needs before it can read its settings from the database. Everything else is
/// configured in the GUI (agents/architecture.md, Configuration principle).
/// </summary>
/// <param name="OwnerConnectionString">ASKWERK_DB: connection string of the database owner.</param>
/// <param name="DataKey">ASKWERK_DATA_KEY, or the generated key from the data directory.</param>
/// <param name="DataDirectory">ASKWERK_DATA_DIR: persistent directory for files and the generated key.</param>
/// <param name="Version">ASKWERK_VERSION, or the assembly's informational version.</param>
/// <param name="WebRoot">ASKWERK_WEB_ROOT: the built designer app; defaults to wwwroot next to the binaries.</param>
/// <param name="DemoSurvey">ASKWERK_DEMO_SURVEY: every organisation gets the demo survey (AWK-23); on unless "off".</param>
/// <param name="Overrides">Installation settings fixed in the configuration (AWK-39); none when null.</param>
public sealed record AskwerkSettings(string OwnerConnectionString, string DataKey, string DataDirectory, string Version, string WebRoot, bool DemoSurvey = true, InstallationOverrides? Overrides = null)
{
    /// <summary>Default data directory inside the container (a volume in deploy/docker-compose.yml).</summary>
    public const string DefaultDataDirectory = "/var/lib/askwerk/files";

    /// <summary>Reads the settings from configuration (environment variables in the container).</summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The settings.</returns>
    public static AskwerkSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var owner = configuration["ASKWERK_DB"]
            ?? throw new InvalidOperationException("ASKWERK_DB is not set. It holds the PostgreSQL connection string (see deploy/.env.example).");
        var dataDirectory = configuration["ASKWERK_DATA_DIR"] ?? DefaultDataDirectory;
        var dataKey = Configuration.DataKey.Resolve(configuration["ASKWERK_DATA_KEY"], dataDirectory);
        var version = configuration["ASKWERK_VERSION"]
            ?? typeof(AskwerkSettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0-dev";

        var webRoot = configuration["ASKWERK_WEB_ROOT"] ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

        // For now the demo survey is always there; it becomes a choice later (AWK-23).
        var demoSurvey = configuration["ASKWERK_DEMO_SURVEY"] is not ("off" or "false" or "0");

        return new AskwerkSettings(owner, dataKey, dataDirectory, version, webRoot, demoSurvey, InstallationOverrides.From(configuration));
    }
}
