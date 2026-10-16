// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Application.Storage;

/// <summary>
/// Where uploaded files live (agents/architecture.md, File storage): a folder on the data volume by default; an
/// S3-compatible endpoint can be added later behind the same interface.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores a file under a key; an existing file with the same key is replaced.</summary>
    /// <param name="key">Relative key, e.g. "{tenant}/{asset}".</param>
    /// <param name="content">The bytes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task.</returns>
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <summary>Opens a stored file for reading; null when there is none.</summary>
    /// <param name="key">The key it was stored under.</param>
    /// <returns>A stream the caller disposes, or null.</returns>
    Stream? OpenRead(string key);

    /// <summary>Deletes a stored file; nothing happens when there is none.</summary>
    /// <param name="key">The key it was stored under.</param>
    void Delete(string key);
}
