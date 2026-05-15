using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using core.Entities;
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
    public async Task GmailConnect_ValidCode_CreatesUserAndReturnsUserId()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult("access-token-123", "refresh-token-456", DateTime.UtcNow.AddHours(1)));

        var request = new { code = "valid-auth-code", firstName = "John", lastName = "Doe" };
        var response = await _client.PostAsJsonAsync("/api/mail-connect/gmail/connect", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var userId = content.GetProperty("userId").GetString();
        Assert.NotNull(userId);
        Assert.True(Guid.TryParse(userId, out _));

        // Verify user was persisted
        using var db = _factory.CreateDbContext();
        var user = await db.Users.FindAsync(Guid.Parse(userId));
        Assert.NotNull(user);
        Assert.Equal("John", user.FirstName);
        Assert.Equal("Doe", user.LastName);
        Assert.Equal("access-token-123", user.AccessToken);
        Assert.Equal("refresh-token-456", user.RefreshToken);
    }

    [Fact]
    public async Task GmailConnect_CallsTokenExchangerWithCode()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult("at", "rt", DateTime.UtcNow.AddHours(1)));

        var request = new { code = "my-special-code", firstName = "Jane", lastName = "Smith" };
        await _client.PostAsJsonAsync("/api/mail-connect/gmail/connect", request);

        await _factory.MockTokenExchanger.Received(1)
            .ExchangeCodeAsync("my-special-code", Arg.Any<CancellationToken>());
    }
}
