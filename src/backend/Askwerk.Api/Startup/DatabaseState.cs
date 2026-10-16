// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Api.Startup;

/// <summary>Whether first start (role, migrations, instance) has completed, and how to reach the database.</summary>
public sealed class DatabaseState(TimeProvider time)
{
    private volatile string? _appConnectionString;

    /// <summary>When this process started (UTC).</summary>
    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

    /// <summary>The app role's connection string once the database is ready; otherwise null.</summary>
    public string? AppConnectionString => _appConnectionString;

    /// <summary>True once the database is migrated and the instance is initialised.</summary>
    public bool IsReady => _appConnectionString is not null;

    /// <summary>Marks the database as ready.</summary>
    /// <param name="appConnectionString">The app role's connection string.</param>
    public void MarkReady(string appConnectionString) => _appConnectionString = appConnectionString;

    /// <summary>The app connection string, or an exception when the database is not ready yet.</summary>
    /// <returns>The connection string.</returns>
    public string RequireAppConnectionString() =>
        _appConnectionString ?? throw new DatabaseNotReadyException();
}

/// <summary>Thrown when a request needs the database before first start has completed; answered with 503.</summary>
public sealed class DatabaseNotReadyException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DatabaseNotReadyException()
        : base("The database is not ready yet.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public DatabaseNotReadyException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public DatabaseNotReadyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
