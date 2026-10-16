// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Tests.Application.Exports;

/// <summary>
/// Compares an export with its golden file in golden/&lt;format&gt;/. Run with ASKWERK_UPDATE_SNAPSHOTS=1 to write the
/// current output instead; the diff is then reviewed like any other change.
/// </summary>
internal static class Golden
{
    public static void AssertMatches(string relativePath, string actual)
    {
        var path = Path.Combine(ProjectDirectory(), "golden", relativePath);

        if (Environment.GetEnvironmentVariable("ASKWERK_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"Golden file {relativePath} does not exist. Run the tests with ASKWERK_UPDATE_SNAPSHOTS=1 and review the new file.");
        Assert.Equal(File.ReadAllText(path), actual);
    }

    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Askwerk.Tests.Application.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Askwerk.Tests.Application.csproj not found above the test binaries.");
    }
}
