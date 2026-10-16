// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;
using Askwerk.Application.Storage;

namespace Askwerk.Infrastructure.Storage;

/// <summary>Files in a folder on the data volume (ASKWERK_DATA_DIR/assets). Keys are checked, so no key leaves the folder.</summary>
/// <param name="root">The folder.</param>
public sealed partial class FolderFileStorage(string root) : IFileStorage
{
    /// <inheritdoc />
    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part";

        await using (var file = File.Create(temporary))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <inheritdoc />
    public Stream? OpenRead(string key)
    {
        var path = PathOf(key);

        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    /// <inheritdoc />
    public void Delete(string key) => File.Delete(PathOf(key));

    private string PathOf(string key)
    {
        if (!Key().IsMatch(key))
        {
            throw new ArgumentException($"Not a storage key: {key}", nameof(key));
        }

        return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    // An upload, a resized copy of one (-w960), or an export's file (-export, AWK-25).
    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}(-w[0-9]{3,4}|-export)?$")]
    private static partial Regex Key();
}
