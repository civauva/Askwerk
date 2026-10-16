// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Askwerk.Tests.Api;

/// <summary>
/// Compares a JSON document with a file committed in the repository. Run with ASKWERK_UPDATE_SNAPSHOTS=1
/// to write the current document instead; the diff is then reviewed like any other change.
/// </summary>
internal static class Snapshot
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void AssertJsonMatches(string repositoryPath, string actualJson)
    {
        var path = Path.Combine(FindRepositoryRoot(), repositoryPath);
        var actual = Normalize(actualJson);

        if (Environment.GetEnvironmentVariable("ASKWERK_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"Snapshot {repositoryPath} does not exist. Run the tests with ASKWERK_UPDATE_SNAPSHOTS=1 and review the new file.");
        var expected = Normalize(File.ReadAllText(path));

        Assert.Equal(expected, actual);
    }

    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (global.json) not found.");
    }
}
