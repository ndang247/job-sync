namespace core.Interfaces;

public interface ISyncHubNotifier
{
    Task SendProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default);
    Task SendCompletedAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task SendFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default);
}
