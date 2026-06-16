using core.Entities;
using core.Enums;
using core.Interfaces;
using Google.Apis.Gmail.v1;
using infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using web_api.Authentication;

namespace web_api.Controllers;

[ApiController]
[Route("api/v1/mail-connect")]
public sealed class MailConnectController(
    AppDbContext dbContext,
    IConfiguration configuration,
    IGoogleTokenExchanger tokenExchanger,
    IGoogleOAuthStateStore stateStore) : ControllerBase
{
    [Authorize]
    [HttpPost("gmail/start")]
    public IActionResult GmailStart()
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var state = stateStore.Issue(userId);
        var scopes = string.Join(" ",
            GmailService.Scope.GmailReadonly,
            "openid",
            "profile",
            "email");

        var authorizationUrl = QueryHelpers.AddQueryString(
            "https://accounts.google.com/o/oauth2/v2/auth",
            new Dictionary<string, string?>
            {
                ["client_id"] = configuration["Google:ClientId"],
                ["redirect_uri"] = configuration["Google:RedirectUri"],
                ["response_type"] = "code",
                ["scope"] = scopes,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["state"] = state
            });

        return Ok(new { authorizationUrl });
    }

    [AllowAnonymous]
    [HttpGet("gmail/callback")]
    public async Task<IActionResult> GmailCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var frontendUrl = configuration["FrontendUrl"]!;
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Redirect(
                $"{frontendUrl}/connect?error={Uri.EscapeDataString(error ?? "no_code")}");
        }

        if (string.IsNullOrEmpty(state) ||
            !stateStore.TryConsume(state, out var userId))
        {
            return BadRequest(new
            {
                code = "INVALID_OAUTH_STATE",
                error = "The OAuth state is invalid or expired."
            });
        }

        var tokenResult = await tokenExchanger.ExchangeCodeAsync(code, cancellationToken);
        var connection = await dbContext.EmailConnections
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                existing =>
                    existing.Provider == EmailConnectionProvider.Gmail &&
                    existing.SubjectId == tokenResult.SubjectId,
                cancellationToken);

        if (connection is not null && connection.UserId != userId)
        {
            return Redirect($"{frontendUrl}/connect?error=gmail_account_already_connected");
        }

        if (connection is null)
        {
            connection = new EmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = EmailConnectionProvider.Gmail,
                SubjectId = tokenResult.SubjectId
            };
            dbContext.EmailConnections.Add(connection);
        }

        connection.Email = tokenResult.Email;
        connection.RefreshToken = tokenResult.RefreshToken;
        connection.GrantedScopes = tokenResult.GrantedScopes;
        connection.Status = EmailConnectionStatus.Active;
        connection.DeletedAt = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Redirect($"{frontendUrl}/?connectionId={connection.Id}");
    }
}
