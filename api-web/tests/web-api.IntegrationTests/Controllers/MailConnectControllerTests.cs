using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace web_api.IntegrationTests.Controllers;

public class MailConnectControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MailConnectControllerTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GmailStart_PlatformAuthNotEnabled_ReturnsServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/v1/mail-connect/gmail/start");

        await AssertPlatformAuthRequiredAsync(response);
    }

    [Fact]
    public async Task GmailCallback_PlatformAuthNotEnabled_ReturnsServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/v1/mail-connect/gmail/callback?code=test-code&state=test-state");

        await AssertPlatformAuthRequiredAsync(response);
    }

    private static async Task AssertPlatformAuthRequiredAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PLATFORM_AUTH_REQUIRED", content.GetProperty("code").GetString());
        Assert.Equal(
            "Platform authentication must be enabled before connecting Gmail.",
            content.GetProperty("error").GetString());
    }
}
