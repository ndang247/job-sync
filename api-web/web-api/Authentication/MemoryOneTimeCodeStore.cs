using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using web_api.Options;

namespace web_api.Authentication;

public sealed class MemoryOneTimeCodeStore : IOneTimeCodeStore
{
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly OtpOptions _options;
    private readonly object[] _locks = Enumerable.Range(0, 256)
        .Select(static _ => new object())
        .ToArray();

    public MemoryOneTimeCodeStore(
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<OtpOptions> options)
    {
        _cache = cache;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public OtpIssueResult TryIssue(string destination, string purpose, string codeHash)
    {
        var key = CreateKey(destination, purpose);
        lock (GetLock(key))
        {
            var now = _timeProvider.GetUtcNow();
            if (_cache.TryGetValue<OtpEntry>(key, out var existing) &&
                existing is not null &&
                now < existing.ResendAvailableAt)
            {
                return OtpIssueResult.Cooldown(existing.ResendAvailableAt - now);
            }

            var entry = new OtpEntry(
                codeHash,
                now.AddSeconds(_options.ExpirationSeconds),
                now.AddSeconds(_options.ResendCooldownSeconds),
                _options.MaxAttempts);

            _cache.Set(
                key,
                entry,
                TimeSpan.FromSeconds(_options.ExpirationSeconds));
            return OtpIssueResult.Success();
        }
    }

    public OtpVerificationStatus Verify(string destination, string purpose, string codeHash)
    {
        var key = CreateKey(destination, purpose);
        lock (GetLock(key))
        {
            if (!_cache.TryGetValue<OtpEntry>(key, out var entry) ||
                entry is null ||
                _timeProvider.GetUtcNow() >= entry.ExpiresAt ||
                entry.AttemptsRemaining <= 0)
            {
                _cache.Remove(key);
                return OtpVerificationStatus.InvalidOrExpired;
            }

            if (!HashesMatch(entry.CodeHash, codeHash))
            {
                entry.AttemptsRemaining--;
                if (entry.AttemptsRemaining <= 0)
                {
                    _cache.Remove(key);
                }

                return OtpVerificationStatus.InvalidOrExpired;
            }

            _cache.Remove(key);
            return OtpVerificationStatus.Succeeded;
        }
    }

    public void Remove(string destination, string purpose)
    {
        var key = CreateKey(destination, purpose);
        lock (GetLock(key))
        {
            _cache.Remove(key);
        }
    }

    private static string CreateKey(string destination, string purpose) =>
        $"otp:{purpose}:{destination}";

    private object GetLock(string key) =>
        _locks[(uint)StringComparer.Ordinal.GetHashCode(key) % _locks.Length];

    private static bool HashesMatch(string expectedHash, string actualHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedHash),
                Convert.FromHexString(actualHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class OtpEntry(
        string codeHash,
        DateTimeOffset expiresAt,
        DateTimeOffset resendAvailableAt,
        int attemptsRemaining)
    {
        public string CodeHash { get; } = codeHash;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public DateTimeOffset ResendAvailableAt { get; } = resendAvailableAt;
        public int AttemptsRemaining { get; set; } = attemptsRemaining;
    }
}
