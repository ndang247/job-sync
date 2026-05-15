using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace infrastructure.Services;

public class GoogleTokenExchanger : IGoogleTokenExchanger
{
    private readonly IConfiguration _configuration;

    public GoogleTokenExchanger(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _configuration["Google:ClientId"]!,
                ClientSecret = _configuration["Google:ClientSecret"]!
            },
            Scopes = [Google.Apis.Gmail.v1.GmailService.Scope.GmailReadonly]
        });

        var tokenResponse = await flow.ExchangeCodeForTokenAsync(
            "user",
            code,
            _configuration["Google:RedirectUri"]!,
            cancellationToken);

        return new OAuthTokenResult(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600));
    }
}
