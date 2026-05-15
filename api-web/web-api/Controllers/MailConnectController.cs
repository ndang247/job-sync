using Google.Apis.Gmail.v1;
using core.Entities;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using api_contracts.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    [HttpPost("gmail/connect")]
    public async Task<IActionResult> GmailConnect([FromBody] GmailConnectRequest request)
    {
        var tokenResult = await _tokenExchanger.ExchangeCodeAsync(request.Code);

        User user;

        if (request.UserId.HasValue)
        {
            var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == request.UserId.Value);
            if (existingUser is null)
                return BadRequest(new { error = "User not found" });
            user = existingUser;
        }
        else
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = request.FirstName,
                LastName = request.LastName,
            };
            _dbContext.Users.Add(user);
        }

        // Upsert EmailConnection by (UserId, SubjectId)
        var existingConnection = await _dbContext.EmailConnections
            .FirstOrDefaultAsync(ec => ec.UserId == user.Id && ec.SubjectId == tokenResult.SubjectId);

        EmailConnection connection;

        if (existingConnection is not null)
        {
            existingConnection.RefreshToken = tokenResult.RefreshToken;
            existingConnection.GrantedScopes = tokenResult.GrantedScopes;
            existingConnection.Email = tokenResult.Email;
            existingConnection.Status = EmailConnectionStatus.Active;
            connection = existingConnection;
        }
        else
        {
            connection = new EmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = tokenResult.Email,
                SubjectId = tokenResult.SubjectId,
                RefreshToken = tokenResult.RefreshToken,
                GrantedScopes = tokenResult.GrantedScopes,
                Status = EmailConnectionStatus.Active
            };
            _dbContext.EmailConnections.Add(connection);
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            userId = user.Id,
            emailConnectionId = connection.Id,
            status = connection.Status.ToString().ToLowerInvariant()
        });
    }
}
