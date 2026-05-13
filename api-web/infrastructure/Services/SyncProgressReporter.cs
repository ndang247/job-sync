using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace infrastructure.Services;

public class SyncProgressReporter : ISyncProgressReporter
{
    private readonly AppDbContext _dbContext;
    private readonly ISyncHubNotifier _hubNotifier;

    public SyncProgressReporter(AppDbContext dbContext, ISyncHubNotifier hubNotifier)
    {
        _dbContext = dbContext;
        _hubNotifier = hubNotifier;
    }

    public async Task ReportProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job != null)
        {
            job.Stage = stage;
            job.Progress = percent;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _hubNotifier.SendProgressAsync(jobId, stage, percent, cancellationToken);
    }

    public async Task ReportCompletedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await _hubNotifier.SendCompletedAsync(jobId, cancellationToken);
    }

    public async Task ReportFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        await _hubNotifier.SendFailedAsync(jobId, error, cancellationToken);
    }
}
