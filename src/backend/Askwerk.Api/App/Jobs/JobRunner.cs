// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Startup;
using Askwerk.Application.Persistence;
using Askwerk.Domain.Jobs;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Jobs;

/// <summary>One kind of background work (architecture.md); the runner calls it inside the job's organisation.</summary>
public interface IJobHandler
{
    /// <summary>The kind it does, e.g. send-invitations.</summary>
    string Kind { get; }

    /// <summary>Does the work; an exception makes the job run again later.</summary>
    /// <param name="payload">The job's JSON.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>When done.</returns>
    Task RunAsync(string payload, CancellationToken cancellationToken);
}

/// <summary>
/// Runs due jobs from the job table (AWK-7; architecture.md): claims a few at a time with FOR UPDATE SKIP LOCKED, so
/// several instances can run side by side, and runs each in its organisation under row-level security.
/// </summary>
public sealed partial class JobRunner(IServiceScopeFactory scopes, DatabaseState state, TimeProvider time, ILogger<JobRunner> logger) : IDisposable
{
    private const int Batch = 10;
    private const int LeaseSeconds = 300;
    private readonly SemaphoreSlim nudged = new(0, 1);

    /// <summary>
    /// Starts the next round now instead of at the next tick, for work someone waits for, such as an export
    /// (AWK-25). Call it after the job is saved.
    /// </summary>
    public void Nudge()
    {
        try
        {
            nudged.Release();
        }
        catch (SemaphoreFullException)
        {
            // A round is already asked for.
        }
    }

    /// <summary>Waits until <see cref="Nudge"/> is called or the time is over.</summary>
    /// <param name="longest">The longest wait.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>When the next round is due.</returns>
    public Task WaitForNextRoundAsync(TimeSpan longest, CancellationToken cancellationToken) => nudged.WaitAsync(longest, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => nudged.Dispose();

    /// <summary>Adds a job; it is saved with the change that causes it.</summary>
    /// <param name="context">The request's database context.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="kind">What to do.</param>
    /// <param name="payload">What the handler reads.</param>
    /// <param name="runAt">Not before.</param>
    /// <param name="now">Now.</param>
    /// <returns>The job's id.</returns>
    public static Guid Enqueue(AskwerkDbContext context, Guid tenantId, string kind, object payload, DateTimeOffset runAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        var job = Job.Create(Guid.CreateVersion7(now), tenantId, kind, JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), runAt, now);
        context.Jobs.Add(job);

        return job.Id;
    }

    /// <summary>Runs every job that is due now, until none is left.</summary>
    /// <param name="cancellationToken">Cancels the round.</param>
    /// <returns>When done.</returns>
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        if (!state.IsReady)
        {
            return;
        }

        while (true)
        {
            List<ClaimedJob> claimed;

            using (var scope = scopes.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AskwerkDbContext>();
                claimed = await context.Database
                    .SqlQuery<ClaimedJob>($"SELECT id AS \"Id\", tenant_id AS \"TenantId\" FROM claim_jobs({Batch}, {LeaseSeconds})")
                    .ToListAsync(cancellationToken);
            }

            if (claimed.Count == 0)
            {
                return;
            }

            foreach (var item in claimed)
            {
                await RunAsync(item, cancellationToken);
            }
        }
    }

    private async Task RunAsync(ClaimedJob item, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<DbSessionContext>().TenantId = item.TenantId;
        var context = scope.ServiceProvider.GetRequiredService<AskwerkDbContext>();
        var job = await context.Jobs.SingleAsync(row => row.Id == item.Id, cancellationToken);
        var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(each => each.Kind == job.Kind);

        try
        {
            if (handler is null)
            {
                throw new InvalidOperationException($"No handler for jobs of kind {job.Kind}.");
            }

            await handler.RunAsync(job.Payload, cancellationToken);
            job.Done(time.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, job.Id, job.Kind, job.Attempts, exception);
            job.Failed(exception.Message, time.GetUtcNow());
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} ({Kind}) failed on try {Attempt}; it runs again later.")]
    private static partial void LogFailed(ILogger logger, Guid jobId, string kind, int attempt, Exception exception);

    private sealed class ClaimedJob
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }
    }
}

/// <summary>Runs <see cref="JobRunner"/> every fifteen seconds, or at once when nudged; ASKWERK_JOBS=manual leaves it to the caller (tests).</summary>
public sealed partial class JobService(JobRunner runner, IConfiguration configuration, ILogger<JobService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.Equals(configuration["ASKWERK_JOBS"], "manual", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await runner.WaitForNextRoundAsync(Every, stoppingToken);

            try
            {
                await runner.RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The job round failed; the next one starts in fifteen seconds.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
