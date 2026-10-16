// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Configuration;
using Askwerk.Application.Instances;
using Askwerk.Application.Storage;
using Askwerk.Application.Telemetry;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Persistence;
using Askwerk.Infrastructure.Seeding;

namespace Askwerk.Api.Startup;

/// <summary>
/// First start in the background: prepares the database and initialises the instance, retrying until the
/// database is reachable. The process answers /health/live meanwhile and /health/ready only afterwards.
/// </summary>
public sealed partial class DatabaseStartupService(
    AskwerkSettings settings,
    DatabaseState state,
    TimeProvider time,
    ITraceContext trace,
    IFileStorage storage,
    ILogger<DatabaseStartupService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var appConnectionString = await DatabaseBootstrapper.EnsureReadyAsync(settings.OwnerConnectionString, settings.DataKey, stoppingToken);
                await InitializeInstanceAsync(appConnectionString, stoppingToken);
                await SeedDemoSurveyAsync(stoppingToken);

                // Ready only after the instance exists, so nothing can observe a half-initialised database.
                state.MarkReady(appConnectionString);

                LogDatabaseReady(logger);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // EF wraps the cause in "likely due to a transient failure"; the inner error says what failed.
                var cause = exception.InnerException ?? exception;
                LogDatabaseNotReady(logger, cause.GetType().Name, cause.Message);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task InitializeInstanceAsync(string appConnectionString, CancellationToken cancellationToken)
    {
        await using var context = AskwerkDbContext.Create(appConnectionString);
        var handler = new InitializeInstanceHandler(
            new EfInstanceRepository(context),
            new EfAuditWriter(context),
            new EfUnitOfWork(context),
            time,
            trace);

        await handler.HandleAsync(cancellationToken);
    }

    /// <summary>Gives organisations without one the demo survey; a failure is logged and never stops the start.</summary>
    private async Task SeedDemoSurveyAsync(CancellationToken cancellationToken)
    {
        if (!settings.DemoSurvey)
        {
            return;
        }

        try
        {
            await DemoSurvey.EnsureAsync(settings.OwnerConnectionString, storage, time, trace.CurrentTraceId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDemoSurveyFailed(logger, exception.GetType().Name, exception.Message);
        }
    }

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Demo survey not created ({ErrorType}: {ErrorMessage}).")]
    private static partial void LogDemoSurveyFailed(ILogger logger, string errorType, string errorMessage);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Database ready; instance initialised.")]
    private static partial void LogDatabaseReady(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Database not ready ({ErrorType}: {ErrorMessage}); retrying.")]
    private static partial void LogDatabaseNotReady(ILogger logger, string errorType, string errorMessage);
}
