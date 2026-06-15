using Microsoft.Extensions.Caching.Memory;
using web_api.Authentication;

namespace web_api.IntegrationTests.Authentication;

public sealed class GoogleOAuthStateStoreTests
{
    [Fact]
    public void State_IsSingleUse()
    {
        var store = CreateStore(out _);
        var userId = Guid.NewGuid();
        var state = store.Issue(userId);

        Assert.True(store.TryConsume(state, out var consumedUserId));
        Assert.Equal(userId, consumedUserId);
        Assert.False(store.TryConsume(state, out _));
    }

    [Fact]
    public void State_ExpiresAfterTenMinutes()
    {
        var store = CreateStore(out var timeProvider);
        var state = store.Issue(Guid.NewGuid());

        timeProvider.Advance(TimeSpan.FromMinutes(10));

        Assert.False(store.TryConsume(state, out _));
    }

    private static GoogleOAuthStateStore CreateStore(
        out MutableTimeProvider timeProvider)
    {
        timeProvider = new MutableTimeProvider();
        return new GoogleOAuthStateStore(
            new MemoryCache(new MemoryCacheOptions()),
            timeProvider);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
