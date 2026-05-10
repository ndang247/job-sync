namespace core.Interfaces;

public interface ISyncProgressReporter
{
    Task ReportProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default);
    Task ReportCompletedAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task ReportFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default);
}
