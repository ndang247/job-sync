using core.Models;

namespace core.Interfaces;

public interface ISyncOrchestrator
{
    Task<List<JobApplication>> ExecuteSyncAsync(Guid jobId, Guid userId, ISyncProgressReporter progressReporter, CancellationToken cancellationToken = default);
}
