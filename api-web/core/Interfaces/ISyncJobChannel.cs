namespace core.Interfaces;

public interface ISyncJobChannel
{
    ValueTask WriteAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken = default);
}
