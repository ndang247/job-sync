using core.Interfaces;
using core.Models;

namespace infrastructure.Services;

public class SyncOrchestrator : ISyncOrchestrator
{
    public Task<List<JobApplication>> ExecuteSyncAsync(Guid jobId, Guid userId, ISyncProgressReporter progressReporter, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
