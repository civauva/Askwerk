// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Askwerk.Infrastructure.Persistence;

/// <summary>
/// Prepares the database on start: ensures the restricted <c>askwerk_app</c> role and runs migrations as
/// the owner. The app then connects as <c>askwerk_app</c>, which may only insert and read audit entries.
/// </summary>
public static class DatabaseBootstrapper
{
    /// <summary>Name of the login role the application uses at runtime.</summary>
    public const string AppRole = "askwerk_app";

    /// <summary>Ensures the app role and the schema, then returns the app connection string.</summary>
    /// <param name="ownerConnectionString">Connection string of the database owner (may create roles).</param>
    /// <param name="dataKey">The instance data key; the app role's password is derived from it.</param>
    /// <param name="cancellationToken">Cancels the preparation.</param>
    /// <returns>The connection string for the app role.</returns>
    public static async Task<string> EnsureReadyAsync(string ownerConnectionString, string dataKey, CancellationToken cancellationToken)
    {
        var password = DeriveAppRolePassword(dataKey);
        var owner = new NpgsqlConnectionStringBuilder(ownerConnectionString);

        await EnsureAppRoleWithRetryAsync(ownerConnectionString, owner.Database!, password, cancellationToken);

        await using (var context = AskwerkDbContext.Create(ownerConnectionString))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        var app = new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = AppRole,
            Password = password,
        };

        return app.ConnectionString;
    }

    /// <summary>Derives the app role's password from the data key, so no extra secret is needed.</summary>
    /// <param name="dataKey">The instance data key.</param>
    /// <returns>A URL-safe password.</returns>
    public static string DeriveAppRolePassword(string dataKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataKey);

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(dataKey), Encoding.UTF8.GetBytes("askwerk-db-app-role-v1"));

        // Base64url without padding: letters, digits, '-' and '_' only, so it is safe inside SQL literals below.
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// The role is server-wide, so two databases (or two processes) preparing at once can update its catalog
    /// row concurrently; PostgreSQL then fails with "tuple concurrently updated". The statements are idempotent, so try again.
    /// </summary>
    private static async Task EnsureAppRoleWithRetryAsync(string ownerConnectionString, string database, string password, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await EnsureAppRoleAsync(ownerConnectionString, database, password, cancellationToken);
                return;
            }
            catch (PostgresException error) when (attempt < 10 && IsConcurrentCatalogUpdate(error))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsConcurrentCatalogUpdate(PostgresException error) =>
        error.SqlState == PostgresErrorCodes.SerializationFailure
        || (error.SqlState == PostgresErrorCodes.InternalError && error.MessageText.Contains("concurrently", StringComparison.OrdinalIgnoreCase));

    private static async Task EnsureAppRoleAsync(string ownerConnectionString, string database, string password, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Role DDL cannot take parameters; the password is base64url and the names are constants or quoted.
        var sql = $"""
            DO $$
            BEGIN
              CREATE ROLE {AppRole} LOGIN PASSWORD '{password}';
            EXCEPTION
              -- Roles are server-wide: another database or a second Askwerk process may have created it.
              WHEN duplicate_object OR unique_violation THEN
                ALTER ROLE {AppRole} LOGIN PASSWORD '{password}';
            END
            $$;
            GRANT CONNECT ON DATABASE {QuoteIdentifier(database)} TO {AppRole};
            GRANT USAGE ON SCHEMA public TO {AppRole};
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
