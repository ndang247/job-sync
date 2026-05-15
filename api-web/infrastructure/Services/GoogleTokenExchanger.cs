using System.IdentityModel.Tokens.Jwt;
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
            Scopes = [
                Google.Apis.Gmail.v1.GmailService.Scope.GmailReadonly,
                "openid",
                "profile",
                "email"
            ]
        });

        var tokenResponse = await flow.ExchangeCodeForTokenAsync(
            "user",
            code,
            _configuration["Google:RedirectUri"]!,
            cancellationToken);

        var subjectId = string.Empty;
        var email = string.Empty;
        var givenName = string.Empty;
        var familyName = string.Empty;

        if (!string.IsNullOrEmpty(tokenResponse.IdToken))
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenResponse.IdToken);
            subjectId = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ?? string.Empty;
            email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? string.Empty;
            givenName = jwt.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value ?? string.Empty;
            familyName = jwt.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value ?? string.Empty;
        }

        var grantedScopes = tokenResponse.Scope ?? Google.Apis.Gmail.v1.GmailService.Scope.GmailReadonly;

        return new OAuthTokenResult(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600),
            subjectId,
            email,
            givenName,
            familyName,
            grantedScopes);
    }
}
