using System.Text.Json;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace worker;

public class SyncBackgroundService : BackgroundService
{
    // BackgroundService is a singleton, so we can't inject scoped services (e.g. AppDbContext) directly.
    // IServiceScopeFactory lets us create a new DI scope per polling cycle, ensuring scoped services
    // get fresh instances and are properly disposed after each iteration.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncBackgroundService> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    public SyncBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Poll loop: checks for pending jobs, then sleeps for PollingInterval before checking again.
    // Task.Delay accepts stoppingToken so the delay is cancelled immediately on shutdown.
    //
    // Trade-offs:
    //   - Simple and reliable, but introduces up to 5s latency before a new job is picked up.
    //   - Executes a DB query every 5s even when there's no work (wasted I/O under idle load).
    //
    // Alternatives for improvement:
    //   - Event-driven: use a message queue (e.g. RabbitMQ, Azure Service Bus) or Postgres LISTEN/NOTIFY
    //     so the worker reacts instantly to new jobs with zero idle queries.
    //   - Hybrid: use a signaling mechanism (e.g. SemaphoreSlim, Channel<T>) triggered by the API
    //     when a job is created, with a fallback poll as a safety net.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingJobsAsync(stoppingToken);
            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    public async Task ProcessPendingJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncOrchestrator>();
        var progressReporter = scope.ServiceProvider.GetRequiredService<ISyncProgressReporter>();

        var pendingJobs = await dbContext.SyncJobs
            .Where(j => j.Status == SyncJobStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var job in pendingJobs)
        {
            try
            {
                job.Status = SyncJobStatus.Processing;
                await dbContext.SaveChangesAsync(cancellationToken);

                var results = await orchestrator.ExecuteSyncAsync(job.Id, job.UserId, progressReporter, cancellationToken);

                job.Result = JsonSerializer.SerializeToDocument(results);
                job.Status = SyncJobStatus.Completed;
                await dbContext.SaveChangesAsync(cancellationToken);

                await progressReporter.ReportCompletedAsync(job.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync job {JobId} failed", job.Id);
                job.Status = SyncJobStatus.Failed;
                job.Error = ex.Message;
                await dbContext.SaveChangesAsync(cancellationToken);

                await progressReporter.ReportFailedAsync(job.Id, ex.Message, cancellationToken);
            }
        }
    }
}
