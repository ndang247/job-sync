using Google.Apis.Gmail.v1;
using core.Entities;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace web_api.Controllers;

[ApiController]
[Route("api/v1/mail-connect")]
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

    [HttpGet("gmail/start")]
    public IActionResult GmailStart()
    {
        var clientId = _configuration["Google:ClientId"]!;
        var redirectUri = _configuration["Google:RedirectUri"]!;

        var scopes = $"{GmailService.Scope.GmailReadonly} openid profile email";

        var url = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                  $"client_id={clientId}&" +
                  $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                  $"response_type=code&" +
                  $"scope={Uri.EscapeDataString(scopes)}&" +
                  $"access_type=offline&" +
                  $"prompt=consent";

        return Redirect(url);
    }

    [HttpGet("gmail/callback")]
    public async Task<IActionResult> GmailCallback([FromQuery] string? code, [FromQuery] string? error)
    {
        var frontendUrl = _configuration["FrontendUrl"]!;

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Redirect($"{frontendUrl}/connect?error={Uri.EscapeDataString(error ?? "no_code")}");
        }

        var tokenResult = await _tokenExchanger.ExchangeCodeAsync(code);

        // Find existing user by SubjectId, or create a new one
        var existingConnection = await _dbContext.EmailConnections
            .Include(ec => ec.User)
            .FirstOrDefaultAsync(ec => ec.SubjectId == tokenResult.SubjectId);

        User user;
        EmailConnection connection;

        if (existingConnection is not null)
        {
            // Returning user — update tokens
            user = existingConnection.User;
            existingConnection.RefreshToken = tokenResult.RefreshToken;
            existingConnection.GrantedScopes = tokenResult.GrantedScopes;
            existingConnection.Email = tokenResult.Email;
            existingConnection.Status = EmailConnectionStatus.Active;
            connection = existingConnection;
        }
        else
        {
            // New user
            // This is acceptable for now without authentication
            user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = tokenResult.GivenName,
                LastName = tokenResult.FamilyName,
            };
            _dbContext.Users.Add(user);

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

        return Redirect($"{frontendUrl}/dashboard?userId={user.Id}&connectionId={connection.Id}");
    }
}
