using Microsoft.Extensions.Caching.Memory;
using web_api.Authentication;
using web_api.Options;

namespace web_api.IntegrationTests.Authentication;

public sealed class OneTimeCodeTests
{
    private static readonly string FirstHash = new('A', 64);
    private static readonly string SecondHash = new('B', 64);
    private static readonly string WrongHash = new('C', 64);

    [Fact]
    public void Generator_ReturnsSixNumericDigits()
    {
        var generator = new OneTimeCodeGenerator();

        for (var i = 0; i < 100; i++)
        {
            var code = generator.Generate();

            Assert.Matches(@"^\d{6}$", code);
        }
    }

    [Fact]
    public void Hasher_BindsCodeToDestinationAndPurpose()
    {
        var hasher = new OneTimeCodeHasher(
            Microsoft.Extensions.Options.Options.Create(
                new OtpOptions { Pepper = new string('p', 32) }));

        var first = hasher.Hash("person@example.com", "email-login", "123456");
        var same = hasher.Hash("person@example.com", "email-login", "123456");
        var differentDestination = hasher.Hash("other@example.com", "email-login", "123456");
        var differentPurpose = hasher.Hash("person@example.com", "sms-login", "123456");

        Assert.Equal(first, same);
        Assert.NotEqual(first, differentDestination);
        Assert.NotEqual(first, differentPurpose);
        Assert.True(hasher.Verify(first, "person@example.com", "email-login", "123456"));
        Assert.False(hasher.Verify(first, "person@example.com", "email-login", "654321"));
    }

    [Fact]
    public void Store_EnforcesCooldownAndNewestCodeReplacement()
    {
        var timeProvider = new MutableTimeProvider();
        var store = CreateStore(timeProvider);

        var issued = store.TryIssue("person@example.com", "email-login", FirstHash);
        var throttled = store.TryIssue("person@example.com", "email-login", SecondHash);

        Assert.True(issued.Succeeded);
        Assert.False(throttled.Succeeded);
        Assert.Equal(TimeSpan.FromSeconds(60), throttled.RetryAfter);

        timeProvider.Advance(TimeSpan.FromSeconds(60));
        var replaced = store.TryIssue("person@example.com", "email-login", SecondHash);

        Assert.True(replaced.Succeeded);
        Assert.Equal(OtpVerificationStatus.InvalidOrExpired,
            store.Verify("person@example.com", "email-login", FirstHash));
        Assert.Equal(OtpVerificationStatus.Succeeded,
            store.Verify("person@example.com", "email-login", SecondHash));
    }

    [Fact]
    public void Store_ExpiresCodeAndConsumesSuccessfulVerification()
    {
        var timeProvider = new MutableTimeProvider();
        var store = CreateStore(timeProvider);

        store.TryIssue("person@example.com", "email-login", FirstHash);
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(OtpVerificationStatus.InvalidOrExpired,
            store.Verify("person@example.com", "email-login", FirstHash));

        timeProvider = new MutableTimeProvider();
        store = CreateStore(timeProvider);
        store.TryIssue("person@example.com", "email-login", FirstHash);

        Assert.Equal(OtpVerificationStatus.Succeeded,
            store.Verify("person@example.com", "email-login", FirstHash));
        Assert.Equal(OtpVerificationStatus.InvalidOrExpired,
            store.Verify("person@example.com", "email-login", FirstHash));
    }

    [Fact]
    public void Store_RejectsCodeAfterFiveFailedAttempts()
    {
        var store = CreateStore(new MutableTimeProvider());
        store.TryIssue("person@example.com", "email-login", FirstHash);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(OtpVerificationStatus.InvalidOrExpired,
                store.Verify("person@example.com", "email-login", WrongHash));
        }

        Assert.Equal(OtpVerificationStatus.InvalidOrExpired,
            store.Verify("person@example.com", "email-login", FirstHash));
    }

    [Fact]
    public async Task Store_AllowsOnlyOneConcurrentSuccessfulVerification()
    {
        var store = CreateStore(new MutableTimeProvider());
        store.TryIssue("person@example.com", "email-login", FirstHash);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() =>
                store.Verify("person@example.com", "email-login", FirstHash))));

        Assert.Single(results, result => result == OtpVerificationStatus.Succeeded);
    }

    private static MemoryOneTimeCodeStore CreateStore(TimeProvider timeProvider)
    {
        return new MemoryOneTimeCodeStore(
            new MemoryCache(new MemoryCacheOptions()),
            timeProvider,
            Microsoft.Extensions.Options.Options.Create(new OtpOptions
            {
                Pepper = new string('p', 32),
                ExpirationSeconds = 300,
                ResendCooldownSeconds = 60,
                MaxAttempts = 5
            }));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
