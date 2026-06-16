using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace web_api.IntegrationTests.Authentication;

public sealed class AuthControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RequestOtp_InvalidEmail_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/otp/request", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestOtp_NewEmail_ReturnsGenericAcceptedWithoutCreatingUser()
    {
        var email = UniqueEmail();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/otp/request", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("If the address can receive email, a verification code has been sent.",
            content.GetProperty("message").GetString());
        Assert.Equal(300, content.GetProperty("expiresInSeconds").GetInt32());
        Assert.Equal(60, content.GetProperty("resendAfterSeconds").GetInt32());
        Assert.True(_factory.EmailSender.TryGetCode(email, out _));

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    [Fact]
    public async Task RequestOtp_DeliveryFailure_RemovesCodeAndAllowsImmediateRetry()
    {
        var email = UniqueEmail();
        _factory.EmailSender.FailNextDeliveryTo(email);

        var failed = await _client.PostAsJsonAsync("/api/v1/auth/otp/request", new { email });
        var retry = await _client.PostAsJsonAsync("/api/v1/auth/otp/request", new { email });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var content = await failed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OTP_DELIVERY_FAILED", content.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_ValidCode_CreatesConfirmedUserAndReturnsTokens()
    {
        var email = UniqueEmail();
        await RequestCodeAsync(email);
        Assert.True(_factory.EmailSender.TryGetCode(email, out var code));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { email, code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", content.GetProperty("tokenType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("accessToken").GetString()));
        var accessToken = content.GetProperty("accessToken").GetString()!;
        Assert.False(content.TryGetProperty("refreshToken", out _));
        var refreshToken = ReadRefreshCookie(response);
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        Assert.Equal(900, content.GetProperty("expiresInSeconds").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(email, jwt.Claims.Single(claim => claim.Type == "email").Value);
        Assert.False(string.IsNullOrWhiteSpace(
            jwt.Claims.Single(claim => claim.Type == "jti").Value));

        using var db = _factory.CreateDbContext();
        var storedRefreshToken = db.RefreshTokens.Single(token => token.UserId == user.Id);
        Assert.NotEqual(refreshToken, storedRefreshToken.TokenHash);
        Assert.Equal(64, storedRefreshToken.TokenHash.Length);
    }

    [Fact]
    public async Task VerifyOtp_InvalidCode_ReturnsGenericUnauthorized()
    {
        var email = UniqueEmail();
        await RequestCodeAsync(email);

        var response = await _client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { email, code = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OTP_INVALID_OR_EXPIRED", content.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_RotatesTokenAndRejectsReplay()
    {
        var tokens = await AuthenticateAsync();

        var firstRefresh = await PostWithRefreshCookieAsync(
            "/api/v1/auth/token/refresh",
            tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var rotated = await ReadTokensAsync(firstRefresh);
        Assert.NotEqual(tokens.RefreshToken, rotated.RefreshToken);

        var replay = await PostWithRefreshCookieAsync(
            "/api/v1/auth/token/refresh",
            tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var familyRevoked = await PostWithRefreshCookieAsync(
            "/api/v1/auth/token/refresh",
            rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, familyRevoked.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesFamilyAndIsIdempotent()
    {
        var tokens = await AuthenticateAsync();

        var logout = await PostWithRefreshCookieAsync("/api/v1/auth/logout", tokens.RefreshToken);
        var repeatedLogout = await PostWithRefreshCookieAsync(
            "/api/v1/auth/logout",
            tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedLogout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("job_sync_refresh=;", StringComparison.Ordinal));

        var refresh = await PostWithRefreshCookieAsync(
            "/api/v1/auth/token/refresh",
            tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private async Task RequestCodeAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/otp/request", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<TokenPair> AuthenticateAsync()
    {
        var email = UniqueEmail();
        await RequestCodeAsync(email);
        Assert.True(_factory.EmailSender.TryGetCode(email, out var code));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { email, code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadTokensAsync(response);
    }

    private async Task<HttpResponseMessage> PostWithRefreshCookieAsync(
        string path,
        string refreshToken)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", $"job_sync_refresh={refreshToken}");
        request.Content = JsonContent.Create(new { });
        return await client.SendAsync(request);
    }

    private static async Task<TokenPair> ReadTokensAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TokenPair(
            content.GetProperty("accessToken").GetString()!,
            ReadRefreshCookie(response));
    }

    private static string ReadRefreshCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders));
        var refreshCookie = setCookieHeaders.Single(header =>
            header.StartsWith("job_sync_refresh=", StringComparison.Ordinal));
        var cookieValue = refreshCookie.Split(';', 2)[0];
        return cookieValue["job_sync_refresh=".Length..];
    }

    private static string UniqueEmail() => $"auth-{Guid.NewGuid():N}@example.com";

    private sealed record TokenPair(string AccessToken, string RefreshToken);
}
