// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;

namespace Askwerk.Api.Configuration;

/// <summary>
/// The instance data key that protects secrets stored in the database. Taken from ASKWERK_DATA_KEY, or
/// generated on first start and kept in the data directory so a blank .env still works.
/// </summary>
public static class DataKey
{
    /// <summary>File in the data directory that holds a generated key.</summary>
    public const string FileName = "data-key";

    /// <summary>Returns the configured key, the stored key, or a newly generated and stored key.</summary>
    /// <param name="configured">Value of ASKWERK_DATA_KEY; null or blank when not set.</param>
    /// <param name="dataDirectory">The persistent data directory (a Docker volume in the default install).</param>
    /// <returns>The key to use.</returns>
    public static string Resolve(string? configured, string dataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var path = Path.Combine(dataDirectory, FileName);

        if (File.Exists(path))
        {
            return File.ReadAllText(path).Trim();
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(path, generated);

        return generated;
    }
}
