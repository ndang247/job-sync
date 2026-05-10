using core.Interfaces;

namespace infrastructure.Services;

public class SyncProgressReporter : ISyncProgressReporter
{
    public Task ReportProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task ReportCompletedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task ReportFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
