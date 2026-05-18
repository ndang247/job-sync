using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using core.Entities;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Google.Apis.Util;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace infrastructure.Services;

public partial class GmailService : IEmailService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GmailService> _logger;

    public GmailService(AppDbContext dbContext, IConfiguration configuration, ILogger<GmailService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<EmailMessage>> FetchEmailsAsync(Guid emailConnectionId, CancellationToken cancellationToken = default)
    {
        var connection = await _dbContext.EmailConnections.FirstOrDefaultAsync(
            ec => ec.Id == emailConnectionId, cancellationToken)
            ?? throw new InvalidOperationException($"EmailConnection {emailConnectionId} not found");

        var credential = await GetCredentialAsync(connection, cancellationToken);

        using var gmailService = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Job-Sync"
        });

        var after = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var request = gmailService.Users.Messages.List("me");
        request.Q = $"after:{after}";
        request.MaxResults = 500;

        var emails = new List<EmailMessage>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Messages != null)
            {
                foreach (var msgRef in response.Messages)
                {
                    var msg = await gmailService.Users.Messages.Get("me", msgRef.Id).ExecuteAsync(cancellationToken);
                    emails.Add(ParseMessage(msg));
                }
            }

            pageToken = response.NextPageToken;
        } while (pageToken != null);

        return emails;
    }

    private async Task<UserCredential> GetCredentialAsync(EmailConnection connection, CancellationToken cancellationToken)
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

        // Build credential from refresh token only — access token minted at runtime
        var token = new TokenResponse
        {
            RefreshToken = connection.RefreshToken,
            ExpiresInSeconds = 0 // force refresh
        };

        var credential = new UserCredential(flow, "user", token);

        try
        {
            await credential.RefreshTokenAsync(cancellationToken);
        }
        catch (TokenResponseException ex) when (
            ex.Error?.Error == "invalid_grant" ||
            ex.Error?.Error == "unauthorized_client")
        {
            connection.Status = EmailConnectionStatus.NeedsReconnect;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Email connection requires grant/reconnect.");
        }

        return credential;
    }

    private EmailMessage ParseMessage(Message message)
    {
        var headers = message.Payload?.Headers ?? [];
        var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "";
        var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "";
        var dateStr = headers.FirstOrDefault(h => h.Name == "Date")?.Value ?? "";

        var cleanedDate = ParenthesesRegex().Replace(dateStr, "");
        DateTimeOffset.TryParse(cleanedDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
        if (date == default)
        {
            _logger.LogWarning("Failed to parse date '{DateStr}' for email '{Subject}' from '{From}'", dateStr, subject, from);
        }
        var body = GetBody(message.Payload);

        return new EmailMessage
        {
            Subject = subject,
            From = from,
            Date = DateOnly.FromDateTime(date.DateTime),
            Body = body
        };
    }

    private static string GetBody(MessagePart? payload)
    {
        if (payload == null) return string.Empty;

        if ((payload.MimeType == "text/plain" || payload.MimeType == "text/html")
            && !string.IsNullOrEmpty(payload.Body?.Data))
        {
            return DecodeBase64Url(payload.Body.Data);
        }

        if (payload.Parts == null) return string.Empty;

        foreach (var part in payload.Parts)
        {
            if ((part.MimeType == "text/plain" || part.MimeType == "text/html") && !string.IsNullOrEmpty(part.Body?.Data))
                return DecodeBase64Url(part.Body.Data);
        }

        // Recurse into nested multipart parts
        foreach (var part in payload.Parts)
        {
            var body = GetBody(part);
            if (!string.IsNullOrEmpty(body))
                return body;
        }

        return string.Empty;
    }

    private static string DecodeBase64Url(string input)
    {
        var data = input.Replace('-', '+').Replace('_', '/');
        switch (data.Length % 4)
        {
            case 2: data += "=="; break;
            case 3: data += "="; break;
        }
        var bytes = Convert.FromBase64String(data);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\(.*?\)\s*$")]
    private static partial System.Text.RegularExpressions.Regex ParenthesesRegex();
}
