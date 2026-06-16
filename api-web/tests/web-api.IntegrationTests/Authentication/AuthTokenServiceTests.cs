using core.Entities;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using web_api.Authentication;
using web_api.Options;

namespace web_api.IntegrationTests.Authentication;

public sealed class AuthTokenServiceTests
{
    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsNull()
    {
        var timeProvider = new MutableTimeProvider();
        await using var dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "expired@example.com",
            Email = "expired@example.com",
            EmailConfirmed = true,
            FirstName = string.Empty,
            LastName = string.Empty
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        var service = new AuthTokenService(
            dbContext,
            timeProvider,
            Microsoft.Extensions.Options.Options.Create(new JwtOptions
            {
                Issuer = "tests",
                Audience = "test-clients",
                SigningKey = "test-jwt-signing-key-with-at-least-32-characters",
                AccessTokenMinutes = 15,
                RefreshTokenDays = 30
            }));
        var tokens = await service.IssueAsync(user);

        timeProvider.Advance(TimeSpan.FromDays(30));

        Assert.Null(await service.RefreshAsync(tokens.RefreshToken));
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"AuthTokenTests-{Guid.NewGuid():N}")
                .Options);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
