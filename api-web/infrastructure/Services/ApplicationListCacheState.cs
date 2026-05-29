using core.Interfaces;

namespace infrastructure.Services;

public sealed class ApplicationListCacheState : IApplicationListCacheState
{
    private long _version;

    public long Version => Volatile.Read(ref _version);

    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
    }
}
