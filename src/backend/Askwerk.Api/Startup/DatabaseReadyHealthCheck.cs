// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Askwerk.Api.Startup;

/// <summary>Ready when first start has completed and the database answers right now.</summary>
public sealed class DatabaseReadyHealthCheck(DatabaseState state) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (state.AppConnectionString is not { } connectionString)
        {
            return HealthCheckResult.Unhealthy("First start has not completed.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("Database not reachable.", exception);
        }
    }
}
