// SPDX-License-Identifier: AGPL-3.0-only
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Askwerk.Infrastructure.Persistence;

/// <summary>
/// Puts the session's tenant and user on every connection EF opens, so PostgreSQL's row-level security
/// policies (askwerk.tenant_id, askwerk.user_id) see them. Pooled connections are always overwritten, never reused as-is.
/// </summary>
internal sealed class SessionContextInterceptor(DbSessionContext session) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('askwerk.tenant_id', @tenant, false), set_config('askwerk.user_id', @user, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand CreateCommand(DbConnection connection)
    {
        var command = new NpgsqlCommand(Sql, (NpgsqlConnection)connection);
        command.Parameters.AddWithValue("tenant", session.TenantId?.ToString() ?? "");
        command.Parameters.AddWithValue("user", session.UserId?.ToString() ?? "");

        return command;
    }
}
