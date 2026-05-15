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
    // IServiceScopeFactory lets us create a new DI scope per job, ensuring each job gets its own
    // DbContext and services that are properly disposed after completion.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobChannel _channel;
    private readonly ILogger<SyncBackgroundService> _logger;

    public SyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ISyncJobChannel channel,
        ILogger<SyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _channel = channel;
        _logger = logger;
    }

    // Event-driven: reads job IDs from the channel as soon as the API writes them.
    // Each job is processed concurrently in its own Task with its own DI scope,
    // so multiple users syncing simultaneously are handled in parallel.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOrphanedJobsAsync(stoppingToken);

        await foreach (var jobId in _channel.ReadAllAsync(stoppingToken))
        {
            _ = ProcessJobAsync(jobId, stoppingToken);
        }
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orphanedIds = await dbContext.SyncJobs
            .Where(j => j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Processing)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in orphanedIds)
        {
            _logger.LogInformation("Recovering orphaned job {JobId}", id);
            await _channel.WriteAsync(id, cancellationToken);
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncOrchestrator>();
            var progressReporter = scope.ServiceProvider.GetRequiredService<ISyncProgressReporter>();

            var job = await dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job is null || job.Status != SyncJobStatus.Pending)
                return;

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
            _logger.LogError(ex, "Sync job {JobId} failed", jobId);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var progressReporter = scope.ServiceProvider.GetRequiredService<ISyncProgressReporter>();

                var job = await dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
                if (job is not null)
                {
                    job.Status = SyncJobStatus.Failed;
                    job.Error = ex.Message;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await progressReporter.ReportFailedAsync(jobId, ex.Message, cancellationToken);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to update error status for job {JobId}", jobId);
            }
        }
    }
}
