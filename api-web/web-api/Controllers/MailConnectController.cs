using Google.Apis.Gmail.v1;
using core.Entities;
using core.Interfaces;
using infrastructure.Data;
using api_contracts.Requests;
using Microsoft.AspNetCore.Mvc;

namespace web_api.Controllers;

[ApiController]
[Route("api/mail-connect")]
public class MailConnectController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IGoogleTokenExchanger _tokenExchanger;

    public MailConnectController(AppDbContext dbContext, IConfiguration configuration, IGoogleTokenExchanger tokenExchanger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _tokenExchanger = tokenExchanger;
    }

    [HttpGet("gmail/url")]
    public IActionResult GetGmailUrl()
    {
        var clientId = _configuration["Google:ClientId"]!;
        var redirectUri = _configuration["Google:RedirectUri"]!;

        var url = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                  $"client_id={clientId}&" +
                  $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                  $"response_type=code&" +
                  $"scope={Uri.EscapeDataString(GmailService.Scope.GmailReadonly)}&" +
                  $"access_type=offline&" +
                  $"prompt=consent";

        return Ok(new { url });
    }

    // TODO: Future iteration should support connecting multiple accounts of the same email domain
    // (e.g. multiple Gmail accounts) belonging to a single authenticated user.
    [HttpPost("gmail/connect")]
    public async Task<IActionResult> GmailConnect([FromBody] GmailConnectRequest request)
    {
        var tokenResult = await _tokenExchanger.ExchangeCodeAsync(request.Code);

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            AccessToken = tokenResult.AccessToken,
            RefreshToken = tokenResult.RefreshToken,
            TokenExpiresAt = tokenResult.ExpiresAtUtc
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return Ok(new { userId = user.Id });
    }
}
