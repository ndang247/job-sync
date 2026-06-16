using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using web_api.Interfaces;

namespace web_api.Authentication;

public sealed class GoogleOAuthStateStore(
    IMemoryCache cache,
    TimeProvider timeProvider) : IGoogleOAuthStateStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly object[] _locks = Enumerable.Range(0, 256)
        .Select(static _ => new object())
        .ToArray();

    public string Issue(Guid userId)
    {
        var state = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        cache.Set(
            CreateKey(state),
            new StateEntry(userId, timeProvider.GetUtcNow().Add(Lifetime)),
            Lifetime);
        return state;
    }

    public bool TryConsume(string state, out Guid userId)
    {
        userId = default;
        var key = CreateKey(state);
        lock (GetLock(key))
        {
            if (!cache.TryGetValue<StateEntry>(key, out var entry) ||
                entry is null ||
                timeProvider.GetUtcNow() >= entry.ExpiresAt)
            {
                cache.Remove(key);
                return false;
            }

            cache.Remove(key);
            userId = entry.UserId;
            return true;
        }
    }

    private static string CreateKey(string state) => $"google-oauth-state:{state}";

    private object GetLock(string key) =>
        _locks[(uint)StringComparer.Ordinal.GetHashCode(key) % _locks.Length];

    private sealed record StateEntry(Guid UserId, DateTimeOffset ExpiresAt);
}
