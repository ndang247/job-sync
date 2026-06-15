using System.Net;

namespace web_api.IntegrationTests.Authentication;

public sealed class AuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthorizationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/applications")]
    [InlineData("/api/v1/connections")]
    [InlineData("/api/v1/sync/status/00000000-0000-0000-0000-000000000001")]
    public async Task ProtectedGetEndpoints_WithoutToken_ReturnUnauthorized(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GmailStart_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync("/api/v1/mail-connect/gmail/start", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
