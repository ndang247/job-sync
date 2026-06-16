using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using core.Entities;
using core.Enums;
using core.Interfaces;
using NSubstitute;

namespace web_api.IntegrationTests.Controllers;

public sealed class MailConnectControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public MailConnectControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GmailStart_WithoutAccessToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient()
            .PostAsync("/api/v1/mail-connect/gmail/start", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GmailStart_Authenticated_ReturnsGoogleAuthorizationUrl()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/v1/mail-connect/gmail/start", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var authorizationUrl = content.GetProperty("authorizationUrl").GetString();
        Assert.NotNull(authorizationUrl);
        Assert.Contains("accounts.google.com", authorizationUrl);
        Assert.Contains("gmail.readonly", authorizationUrl);
        Assert.False(string.IsNullOrWhiteSpace(GetState(authorizationUrl)));
    }

    [Fact]
    public async Task GmailCallback_ValidState_AttachesConnectionToAuthenticatedUser()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var state = await StartAndGetStateAsync(client);

        _factory.MockTokenExchanger.ExchangeCodeAsync(
                "valid-code",
                Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "access-token",
                "refresh-token",
                DateTime.UtcNow.AddHours(1),
                $"subject-{Guid.NewGuid():N}",
                "connected@gmail.com",
                "Connected",
                "User",
                "gmail.readonly"));

        var response = await client.GetAsync(
            $"/api/v1/mail-connect/gmail/callback?code=valid-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("connectionId=", location);
        Assert.DoesNotContain("userId=", location);

        using var db = _factory.CreateDbContext();
        var connection = db.EmailConnections.Single(ec => ec.Email == "connected@gmail.com");
        Assert.Equal(userId, connection.UserId);
        Assert.Equal(EmailConnectionStatus.Active, connection.Status);
    }

    [Fact]
    public async Task GmailCallback_ReusedState_ReturnsBadRequest()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var state = await StartAndGetStateAsync(client);

        _factory.MockTokenExchanger.ExchangeCodeAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "access-token",
                "refresh-token",
                DateTime.UtcNow.AddHours(1),
                $"subject-{Guid.NewGuid():N}",
                $"connected-{Guid.NewGuid():N}@gmail.com",
                "Connected",
                "User",
                "gmail.readonly"));

        await client.GetAsync(
            $"/api/v1/mail-connect/gmail/callback?code=first&state={Uri.EscapeDataString(state)}");
        var replay = await client.GetAsync(
            $"/api/v1/mail-connect/gmail/callback?code=second&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task GmailCallback_GmailIdentityOwnedByAnotherUser_RedirectsWithError()
    {
        var (firstClient, firstUserId) = await CreateAuthenticatedClientAsync();
        var subjectId = $"shared-subject-{Guid.NewGuid():N}";
        var firstState = await StartAndGetStateAsync(firstClient);
        _factory.MockTokenExchanger.ExchangeCodeAsync(
                "first-code",
                Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "access-token",
                "refresh-token",
                DateTime.UtcNow.AddHours(1),
                subjectId,
                "shared@gmail.com",
                "Shared",
                "User",
                "gmail.readonly"));
        await firstClient.GetAsync(
            $"/api/v1/mail-connect/gmail/callback?code=first-code&state={Uri.EscapeDataString(firstState)}");

        var (secondClient, _) = await CreateAuthenticatedClientAsync();
        var secondState = await StartAndGetStateAsync(secondClient);
        _factory.MockTokenExchanger.ExchangeCodeAsync(
                "second-code",
                Arg.Any<CancellationToken>())
            .Returns(new OAuthTokenResult(
                "access-token",
                "other-refresh-token",
                DateTime.UtcNow.AddHours(1),
                subjectId,
                "shared@gmail.com",
                "Shared",
                "User",
                "gmail.readonly"));

        var response = await secondClient.GetAsync(
            $"/api/v1/mail-connect/gmail/callback?code=second-code&state={Uri.EscapeDataString(secondState)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "error=gmail_account_already_connected",
            response.Headers.Location!.ToString());
        using var db = _factory.CreateDbContext();
        Assert.Equal(
            firstUserId,
            db.EmailConnections.Single(connection => connection.SubjectId == subjectId).UserId);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
    {
        var userId = Guid.NewGuid();
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = userId,
                UserName = $"mail-{userId:N}@example.com",
                Email = $"mail-{userId:N}@example.com",
                FirstName = "Mail",
                LastName = "User"
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(userId, allowAutoRedirect: false);
        return (client, userId);
    }

    private static async Task<string> StartAndGetStateAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/mail-connect/gmail/start", null);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        return GetState(content.GetProperty("authorizationUrl").GetString()!);
    }

    private static string GetState(string authorizationUrl)
    {
        var query = HttpUtility.ParseQueryString(new Uri(authorizationUrl).Query);
        return query["state"]!;
    }
}
