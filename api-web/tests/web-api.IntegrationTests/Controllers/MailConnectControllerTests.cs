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
        // Don't follow redirects so we can assert on Location header
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    #region Gmail Connect Tests

    [Fact]
    public async Task GmailStart_RedirectsToGoogle()
    {
        var response = await _client.GetAsync("/api/v1/mail-connect/gmail/start");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!.ToString();
        Assert.Contains("accounts.google.com", location);
        Assert.Contains("response_type=code", location);
        Assert.Contains("access_type=offline", location);
        Assert.Contains("gmail.readonly", location);
    }

    [Fact]
    public async Task GmailCallback_ValidCode_CreatesUserAndRedirects()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "access-token-123", "refresh-token-456", DateTime.UtcNow.AddHours(1),
                "google-sub-new", "test@gmail.com", "John", "Doe", "https://www.googleapis.com/auth/gmail.readonly"));

        var response = await _client.GetAsync("/api/v1/mail-connect/gmail/callback?code=valid-auth-code");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("http://localhost:4200/dashboard", location);
        Assert.Contains("userId=", location);
        Assert.Contains("connectionId=", location);

        // Extract userId from redirect URL
        var uri = new Uri(location);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var userId = Guid.Parse(query["userId"]!);
        var connectionId = Guid.Parse(query["connectionId"]!);

        // Verify user was persisted
        using var db = _factory.CreateDbContext();
        var user = await db.Users.FindAsync(userId);
        Assert.NotNull(user);
        Assert.Equal("John", user.FirstName);
        Assert.Equal("Doe", user.LastName);

        // Verify email connection was persisted
        var conn = await db.EmailConnections.FindAsync(connectionId);
        Assert.NotNull(conn);
        Assert.Equal("refresh-token-456", conn.RefreshToken);
        Assert.Equal("test@gmail.com", conn.Email);
        Assert.Equal(EmailConnectionProvider.Gmail, conn.Provider);
        Assert.Equal(EmailConnectionStatus.Active, conn.Status);
    }

    [Fact]
    public async Task GmailCallback_SameSubjectId_UpsertAndDoesNotCreateNewUser()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "at1", "rt-old", DateTime.UtcNow.AddHours(1),
                "upsert-sub", "same@gmail.com", "Upsert", "Test", "gmail.readonly"));

        var response1 = await _client.GetAsync("/api/v1/mail-connect/gmail/callback?code=code-first");
        var location1 = response1.Headers.Location!.ToString();
        var query1 = System.Web.HttpUtility.ParseQueryString(new Uri(location1).Query);
        var connId1 = query1["connectionId"]!;
        var userId1 = query1["userId"]!;

        // Connect again with same subjectId but new refresh token
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "at2", "rt-new", DateTime.UtcNow.AddHours(1),
                "upsert-sub", "same@gmail.com", "Upsert", "Test", "gmail.readonly"));

        var response2 = await _client.GetAsync("/api/v1/mail-connect/gmail/callback?code=code-second");
        var location2 = response2.Headers.Location!.ToString();
        var query2 = System.Web.HttpUtility.ParseQueryString(new Uri(location2).Query);
        var connId2 = query2["connectionId"]!;
        var userId2 = query2["userId"]!;

        // Same user and connection (upserted, not duplicated)
        Assert.Equal(connId1, connId2);
        Assert.Equal(userId1, userId2);

        // Verify new refresh token
        using var db = _factory.CreateDbContext();
        var conn = await db.EmailConnections.FindAsync(Guid.Parse(connId2));
        Assert.Equal("rt-new", conn!.RefreshToken);
    }

    [Fact]
    public async Task GmailCallback_ErrorParam_RedirectsToFrontendWithError()
    {
        var response = await _client.GetAsync("/api/v1/mail-connect/gmail/callback?error=access_denied");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("http://localhost:4200/connect?error=access_denied", location);
    }

    [Fact]
    public async Task GmailCallback_NoCode_RedirectsToFrontendWithError()
    {
        var response = await _client.GetAsync("/api/v1/mail-connect/gmail/callback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("http://localhost:4200/connect?error=no_code", location);
    }

    [Fact]
    public async Task GmailCallback_CallsTokenExchangerWithCode()
    {
        _factory.MockTokenExchanger.ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "at", "rt", DateTime.UtcNow.AddHours(1),
                "sub-verify", "e@g.com", "Jane", "Smith", "scopes"));

        await _client.GetAsync("/api/v1/mail-connect/gmail/callback?code=my-special-code");

        await _factory.MockTokenExchanger.Received(1)
            .ExchangeCodeAsync("my-special-code", Arg.Any<CancellationToken>());
    }

    #endregion
}
