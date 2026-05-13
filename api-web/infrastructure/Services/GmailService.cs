using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace infrastructure.Services;

public class GmailService : IGmailService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public GmailService(AppDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task<List<EmailMessage>> FetchEmailsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found");

        var credential = await GetCredentialAsync(user, cancellationToken);

        using var gmailService = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Job-Sync"
        });

        var after = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();
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

    private async Task<UserCredential> GetCredentialAsync(core.Entities.User user, CancellationToken cancellationToken)
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

        var token = new TokenResponse
        {
            AccessToken = user.AccessToken,
            RefreshToken = user.RefreshToken,
            ExpiresInSeconds = (long)(user.TokenExpiresAt - DateTime.UtcNow).TotalSeconds
        };

        var credential = new UserCredential(flow, "user", token);

        if (credential.Token.IsStale)
        {
            await credential.RefreshTokenAsync(cancellationToken);
            user.AccessToken = credential.Token.AccessToken;
            user.RefreshToken = credential.Token.RefreshToken ?? user.RefreshToken;
            user.TokenExpiresAt = credential.Token.IssuedUtc.AddSeconds(credential.Token.ExpiresInSeconds ?? 3600);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return credential;
    }

    private static EmailMessage ParseMessage(Message message)
    {
        var headers = message.Payload?.Headers ?? [];
        var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "";
        var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "";
        var dateStr = headers.FirstOrDefault(h => h.Name == "Date")?.Value ?? "";

        DateTime.TryParse(dateStr, out var date);

        var body = GetBody(message.Payload);

        return new EmailMessage
        {
            Subject = subject,
            From = from,
            Date = date,
            Body = body
        };
    }

    private static string GetBody(MessagePart? payload)
    {
        if (payload == null) return string.Empty;

        if (!string.IsNullOrEmpty(payload.Body?.Data))
        {
            return DecodeBase64Url(payload.Body.Data);
        }

        if (payload.Parts != null)
        {
            foreach (var part in payload.Parts)
            {
                if ((part.MimeType == "text/plain" || part.MimeType == "text/html") && !string.IsNullOrEmpty(part.Body?.Data))
                {
                    return DecodeBase64Url(part.Body.Data);
                }
            }
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
}
