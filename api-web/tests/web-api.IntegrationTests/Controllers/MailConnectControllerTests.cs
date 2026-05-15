using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using core.Entities;
using core.Enums;
using core.Interfaces;
using NSubstitute;

namespace web_api.IntegrationTests.Controllers;

public class MailConnectControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public MailConnectControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetGmailUrl_ReturnsOk_WithOAuthUrl()
    {
        var response = await _client.GetAsync("/api/mail-connect/gmail/url");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = content.GetProperty("url").GetString();

        Assert.NotNull(url);
        Assert.Contains("accounts.google.com", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("access_type=offline", url);
    }

    [Fact]
    public async Task GetGmailUrl_ContainsGmailReadonlyScope()
    {
        var response = await _client.GetAsync("/api/mail-connect/gmail/url");
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = content.GetProperty("url").GetString()!;

        Assert.Contains("gmail.readonly", url);
    }

    [Fact]
    public async Task GmailConnect_ValidCode_CreatesUserAndConnection()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "access-token-123", "refresh-token-456", DateTime.UtcNow.AddHours(1),
                "google-sub-123", "test@gmail.com", "https://www.googleapis.com/auth/gmail.readonly"));

        var request = new { code = "valid-auth-code", firstName = "John", lastName = "Doe" };
        var response = await _client.PostAsJsonAsync("/api/mail-connect/gmail/connect", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var userId = content.GetProperty("userId").GetString();
        var emailConnectionId = content.GetProperty("emailConnectionId").GetString();
        Assert.NotNull(userId);
        Assert.NotNull(emailConnectionId);
        Assert.True(Guid.TryParse(userId, out _));
        Assert.True(Guid.TryParse(emailConnectionId, out _));

        // Verify user was persisted
        using var db = _factory.CreateDbContext();
        var user = await db.Users.FindAsync(Guid.Parse(userId));
        Assert.NotNull(user);
        Assert.Equal("John", user.FirstName);
        Assert.Equal("Doe", user.LastName);

        // Verify email connection was persisted
        var conn = await db.EmailConnections.FindAsync(Guid.Parse(emailConnectionId));
        Assert.NotNull(conn);
        Assert.Equal("refresh-token-456", conn.RefreshToken);
        Assert.Equal("test@gmail.com", conn.Email);
        Assert.Equal(EmailConnectionStatus.Active, conn.Status);
    }

    [Fact]
    public async Task GmailConnect_WithExistingUserId_AttachesConnection()
    {
        // Seed a user
        Guid userId;
        using (var db = _factory.CreateDbContext())
        {
            var user = new User { Id = Guid.NewGuid(), FirstName = "Existing", LastName = "User" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "at", "rt", DateTime.UtcNow.AddHours(1),
                "sub-456", "second@gmail.com", "gmail.readonly"));

        var request = new { code = "code2", firstName = "Existing", lastName = "User", userId };
        var response = await _client.PostAsJsonAsync("/api/mail-connect/gmail/connect", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId.ToString(), content.GetProperty("userId").GetString());
        Assert.NotNull(content.GetProperty("emailConnectionId").GetString());
    }

    [Fact]
    public async Task GmailConnect_SameSubjectId_UpsertsConnection()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "at1", "rt-old", DateTime.UtcNow.AddHours(1),
                "same-sub", "same@gmail.com", "gmail.readonly"));

        var request = new { code = "code-first", firstName = "Upsert", lastName = "Test" };
        var response1 = await _client.PostAsJsonAsync("/api/mail-connect/gmail/connect", request);
        var content1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        var connId1 = content1.GetProperty("emailConnectionId").GetString();
        var userId = content1.GetProperty("userId").GetString();

        // Connect again with same subjectId but new refresh token
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "at2", "rt-new", DateTime.UtcNow.AddHours(1),
                "same-sub", "same@gmail.com", "gmail.readonly"));

        var request2 = new { code = "code-second", firstName = "Upsert", lastName = "Test", userId = Guid.Parse(userId!) };
        var response2 = await _client.PostAsJsonAsync("/api/mail-connect/gmail/connect", request2);
        var content2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
        var connId2 = content2.GetProperty("emailConnectionId").GetString();

        // Same connection ID (upserted, not duplicated)
        Assert.Equal(connId1, connId2);

        // Verify new refresh token
        using var db = _factory.CreateDbContext();
        var conn = await db.EmailConnections.FindAsync(Guid.Parse(connId2!));
        Assert.Equal("rt-new", conn!.RefreshToken);
    }

    [Fact]
    public async Task GmailConnect_CallsTokenExchangerWithCode()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "at", "rt", DateTime.UtcNow.AddHours(1),
                "sub", "e@g.com", "scopes"));

        var request = new { code = "my-special-code", firstName = "Jane", lastName = "Smith" };
        await _client.PostAsJsonAsync("/api/mail-connect/gmail/connect", request);

        await _factory.MockTokenExchanger.Received(1)
            .ExchangeCodeAsync("my-special-code", Arg.Any<CancellationToken>());
    }
}
