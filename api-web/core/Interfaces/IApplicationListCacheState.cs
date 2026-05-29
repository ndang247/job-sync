namespace core.Interfaces;

public interface IApplicationListCacheState
{
    long Version { get; }
    void Invalidate();
}
