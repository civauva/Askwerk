// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Configuration;

namespace Askwerk.Tests.Api;

public sealed class DataKeyTests
{
    [Fact]
    public void Resolve_ConfiguredKey_UsesIt()
    {
        var directory = NewDirectory();

        var key = DataKey.Resolve(configured: "configured-key", directory);

        Assert.Equal("configured-key", key);
        Assert.False(File.Exists(Path.Combine(directory, DataKey.FileName)));
    }

    [Fact]
    public void Resolve_NoKeyConfigured_GeneratesAndStoresOne()
    {
        var directory = NewDirectory();

        var key = DataKey.Resolve(configured: "", directory);

        Assert.Equal(44, key.Length);
        Assert.Equal(key, File.ReadAllText(Path.Combine(directory, DataKey.FileName)));
    }

    [Fact]
    public void Resolve_NoKeyConfiguredOnSecondStart_ReusesTheStoredKey()
    {
        var directory = NewDirectory();
        var first = DataKey.Resolve(configured: null, directory);

        var second = DataKey.Resolve(configured: null, directory);

        Assert.Equal(first, second);
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "askwerk-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        return path;
    }
}
