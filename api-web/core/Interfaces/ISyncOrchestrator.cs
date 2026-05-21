using core.Entities;

namespace core.Interfaces;

public interface ISyncOrchestrator
{
    Task<List<JobApplication>> ExecuteSyncAsync(Guid jobId, Guid emailConnectionId, ISyncProgressReporter progressReporter, CancellationToken cancellationToken = default);
}
