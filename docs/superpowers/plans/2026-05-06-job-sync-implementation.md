# Job Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET 10 REST API that syncs job applications from Gmail via Gemini AI classification.

**Architecture:** Async pipeline — client triggers sync, background worker fetches Gmail emails, batches them through Gemini for classification/deduplication, stores results in PostgreSQL. SignalR pushes real-time progress to client; polling endpoint as fallback.

**Tech Stack:** .NET 10, EF Core + Npgsql (PostgreSQL), Google.Apis.Gmail.v1, Google AI SDK, BackgroundService, SignalR

---

## File Structure

```
api-web/
├── api-web.slnx
├── api-contracts/
│   ├── Requests/
│   │   ├── GmailConnectRequest.cs
│   │   └── StartSyncRequest.cs
│   └── api-contracts.csproj
├── web-api/
│   ├── Program.cs
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Controllers/
│   │   ├── MailConnectController.cs
│   │   └── SyncController.cs
│   ├── Hubs/
│   │   └── SyncHub.cs
│   ├── Services/
│   │   └── SyncHubNotifier.cs
│   └── web-api.csproj
├── core/
│   ├── Entities/
│   │   ├── BaseEntity.cs
│   │   ├── User.cs
│   │   └── SyncJob.cs
│   ├── Enums/
│   │   └── SyncJobStatus.cs
│   ├── Models/
│   │   └── JobApplication.cs
│   ├── Interfaces/
│   │   ├── IGmailService.cs
│   │   ├── IGeminiService.cs
│   │   ├── ISyncOrchestrator.cs
│   │   ├── ISyncProgressReporter.cs
│   │   ├── ISyncHubNotifier.cs
│   │   └── ISyncJobChannel.cs
│   └── core.csproj
├── infrastructure/
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/
│   │   │   ├── UserConfiguration.cs
│   │   │   └── SyncJobConfiguration.cs
│   │   └── Migrations/
│   │       ├── 20260510042549_InitialCreate.cs
│   │       ├── 20260510042549_InitialCreate.Designer.cs
│   │       └── AppDbContextModelSnapshot.cs
│   ├── Services/
│   │   ├── GmailService.cs
│   │   ├── GeminiService.cs
│   │   ├── SyncOrchestrator.cs
│   │   ├── SyncProgressReporter.cs
│   │   └── SyncJobChannel.cs
│   └── infrastructure.csproj
└── worker/
    ├── SyncBackgroundService.cs
    └── worker.csproj
```

---

## Task 1: Solution Scaffolding ✅ COMPLETED

> Scaffolded using `dotnet new webapi` + `dotnet new classlib` templates.
> Projects: `web-api/` (API), `core/` (domain), `infrastructure/` (data+services), `worker/` (background).
> Solution: `api-web.slnx`. References: `web-api → worker → infrastructure → core`.

---

## Task 2: Core Domain Models ✅ COMPLETED

> Created: `core/Entities/BaseEntity.cs`, `User.cs`, `SyncJob.cs`, `core/Enums/SyncJobStatus.cs`, `core/Models/JobApplication.cs`.
> Namespaces use `core.*` (e.g. `core.Entities`, `core.Enums`, `core.Models`).

---

## Task 3: Core Interfaces ✅ COMPLETED

> Created: `core/Interfaces/IGmailService.cs`, `IGeminiService.cs`, `ISyncOrchestrator.cs`, `ISyncProgressReporter.cs`.
> `EmailMessage` class lives in `IGmailService.cs`. `ISyncOrchestrator` signature includes `jobId`, `userId`, and `ISyncProgressReporter`.

---

## Task 4: Database Context & Configuration ✅ COMPLETED

> Created: `infrastructure/Data/AppDbContext.cs`, `Configurations/UserConfiguration.cs`, `SyncJobConfiguration.cs`.
> Namespaces use `infrastructure.Data` / `infrastructure.Data.Configurations`.
> Infrastructure stub services also created (throw `NotImplementedException`).

---

## Task 5: PostgreSQL Wiring & User Secrets ✅ COMPLETED

> EF Core wired in Program.cs, user secrets init'd, Design package added, initial migration created and applied.
> Google config section added to appsettings.json. DB connectivity verified.

**Files:**

- Modify: `web-api/Program.cs`
- Modify: `web-api/appsettings.json`

- [x] **Step 1: Init user secrets for web-api project**

```bash
cd web-api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=jobsync;Username=postgres;Password=YOUR_PASSWORD"
```

- [x] **Step 2: Add placeholder in appsettings.json (no real credentials)**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": ""
  }
}
```

- [x] **Step 3: Wire up EF Core in Program.cs**

```csharp
// web-api/Program.cs
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();
```

- [x] **Step 4: Add EF Core Design package to web-api**

```bash
cd web-api
dotnet add package Microsoft.EntityFrameworkCore.Design
```

- [x] **Step 5: Create initial migration**

```bash
dotnet ef migrations add InitialCreate --project infrastructure --startup-project web-api --output-dir Data/Migrations
```

- [x] **Step 6: Apply migration to verify DB connectivity**

```bash
dotnet ef database update --project infrastructure --startup-project web-api
```

Expected: Tables `Users` and `SyncJobs` created in PostgreSQL.

- [x] **Step 7: Verify build**

```bash
dotnet build
```

Expected: Build succeeded.

- [x] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: wire up PostgreSQL with EF Core and user secrets"
```

---

## Task 6: Gmail Service ✅ COMPLETED

> Implemented with OAuth token refresh, email fetching (last 30 days), MIME parsing,
> and full pagination via `NextPageToken` loop.
> Packages added: `Google.Apis.Gmail.v1 1.74.0.4134` (to infrastructure).
> ApplicationName = "Job-Sync". Tests not yet created.

**Files:**

- Created: `infrastructure/Services/GmailService.cs`
- Create: `tests/infrastructure.tests/Services/GmailServiceTests.cs`

- [ ] **Step 1: Write failing test for GmailService**

```csharp
// tests/infrastructure.tests/Services/GmailServiceTests.cs
using core.Entities;
using infrastructure.Data;
using infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace infrastructure.Tests.Services;

public class GmailServiceTests
{
    [Fact]
    public async Task FetchEmailsAsync_ThrowsWhenUserNotFound()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);
        var config = Substitute.For<IConfiguration>();

        var service = new GmailService(context, config);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FetchEmailsAsync(Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/infrastructure.Tests --filter "GmailServiceTests"
```

Expected: FAIL — `GmailService` class not found.

- [ ] **Step 3: Implement GmailService**

```csharp
// infrastructure/Services/GmailService.cs
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
        } while (!string.IsNullOrEmpty(pageToken));

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
            Scopes = new[] { Google.Apis.Gmail.v1.GmailService.Scope.GmailReadonly }
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
        var headers = message.Payload?.Headers ?? new List<MessagePartHeader>();
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
            return DecodeBase64(payload.Body.Data);
        }

        if (payload.Parts != null)
        {
            foreach (var part in payload.Parts)
            {
                if (part.MimeType == "text/plain" && !string.IsNullOrEmpty(part.Body?.Data))
                {
                    return DecodeBase64(part.Body.Data);
                }
            }
            foreach (var part in payload.Parts)
            {
                if (part.MimeType == "text/html" && !string.IsNullOrEmpty(part.Body?.Data))
                {
                    return DecodeBase64(part.Body.Data);
                }
            }
        }

        return string.Empty;
    }

    private static string DecodeBase64(string input)
    {
        var data = input.Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(data);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/infrastructure.Tests --filter "GmailServiceTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement Gmail service with token refresh"
```

---

## Task 7: Gemini Service ✅ COMPLETED

> Implemented with batched classification and deduplication via Gemini 2.0 Flash.
> Packages added: `Google_GenerativeAI 3.6.6` (to infrastructure).
> JSON response parsing with markdown fence stripping. Tests not yet created.

**Files:**

- Created: `infrastructure/Services/GeminiService.cs`
- Create: `tests/infrastructure.tests/Services/GeminiServiceTests.cs`

- [ ] **Step 1: Write failing test for batch classification**

```csharp
// tests/infrastructure.tests/Services/GeminiServiceTests.cs
using core.Interfaces;
using infrastructure.Services;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace infrastructure.Tests.Services;

public class GeminiServiceTests
{
    [Fact]
    public async Task ClassifyBatchAsync_ReturnsEmptyList_WhenNoEmails()
    {
        var config = Substitute.For<IConfiguration>();
        config["Google:GeminiApiKey"].Returns("test-key");

        var service = new GeminiService(config);

        var result = await service.ClassifyBatchAsync(new List<EmailMessage>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task DeduplicateAsync_ReturnsEmptyList_WhenNoApplications()
    {
        var config = Substitute.For<IConfiguration>();
        config["Google:GeminiApiKey"].Returns("test-key");

        var service = new GeminiService(config);

        var result = await service.DeduplicateAsync(new List<Core.Models.JobApplication>());

        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/infrastructure.Tests --filter "GeminiServiceTests"
```

Expected: FAIL — `GeminiService` class not found.

- [ ] **Step 3: Implement GeminiService**

````csharp
// infrastructure/Services/GeminiService.cs
using System.Text.Json;
using GenerativeAI;
using core.Interfaces;
using core.Models;
using Microsoft.Extensions.Configuration;

namespace infrastructure.Services;

public class GeminiService : IGeminiService
{
    private readonly IConfiguration _configuration;

    public GeminiService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<List<JobApplication>> ClassifyBatchAsync(List<EmailMessage> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
            return new List<JobApplication>();

        var model = CreateModel();

        var emailsText = string.Join("\n---\n", emails.Select(e =>
            $"Subject: {e.Subject}\nFrom: {e.From}\nDate: {e.Date:dd-MM-yyyy}\nBody: {e.Body[..Math.Min(e.Body.Length, 500)]}"));

        var prompt = $"""
            Given these emails, identify which are job application related.
            For duplicates about the same application (e.g. platform confirmation like Seek.com.au + company auto-reply), return only one entry.
            Return ONLY a JSON array (no markdown, no explanation) with objects containing: companyName, jobRole, appliedDate (use the email date in dd-MM-yyyy format), status (always "applied").
            If no emails are job-related, return an empty array [].

            Emails:
            {emailsText}
            """;

        var response = await model.GenerateContentAsync(prompt, cancellationToken: cancellationToken);
        return ParseResponse(response.Text ?? "[]");
    }

    public async Task<List<JobApplication>> DeduplicateAsync(List<JobApplication> applications, CancellationToken cancellationToken = default)
    {
        if (applications.Count == 0)
            return new List<JobApplication>();

        var model = CreateModel();

        var applicationsJson = JsonSerializer.Serialize(applications);

        var prompt = $"""
            Given these job application results from multiple batches, deduplicate entries for the same company+role combination.
            Return ONLY the final consolidated JSON array (no markdown, no explanation).

            Applications:
            {applicationsJson}
            """;

        var response = await model.GenerateContentAsync(prompt, cancellationToken: cancellationToken);
        return ParseResponse(response.Text ?? "[]");
    }

    private GenerativeModel CreateModel()
    {
        var apiKey = _configuration["Google:GeminiApiKey"]!;
        var genAi = new GoogleAi(apiKey);
        return genAi.CreateGenerativeModel(GoogleAIModels.Gemini2Flash);
    }

    private static List<JobApplication> ParseResponse(string responseText)
    {
        var cleaned = responseText.Trim();
        if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Split('\n', 2).Last();
            cleaned = cleaned[..cleaned.LastIndexOf("```")];
        }
        cleaned = cleaned.Trim();

        try
        {
            return JsonSerializer.Deserialize<List<JobApplication>>(cleaned, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? new List<JobApplication>();
        }
        catch (JsonException)
        {
            return new List<JobApplication>();
        }
    }
}
````

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/infrastructure.Tests --filter "GeminiServiceTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement Gemini service for email classification and deduplication"
```

---

## Task 8: Sync Orchestrator ✅ COMPLETED

> Implemented with batching (20 per batch), progress reporting, and deduplication.
> Final implementation done as part of Task 15 (with progress reporting).

**Files:**

- Created: `infrastructure/Services/SyncOrchestrator.cs`
- Create: `tests/infrastructure.tests/Services/SyncOrchestratorTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/infrastructure.tests/Services/SyncOrchestratorTests.cs
using core.Interfaces;
using core.Models;
using infrastructure.Services;
using NSubstitute;

namespace infrastructure.Tests.Services;

public class SyncOrchestratorTests
{
    [Fact]
    public async Task ExecuteSyncAsync_ReturnsEmpty_WhenNoEmails()
    {
        var gmailService = Substitute.For<IGmailService>();
        var geminiService = Substitute.For<IGeminiService>();
        var progressReporter = Substitute.For<ISyncProgressReporter>();

        gmailService.FetchEmailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmailMessage>());

        var orchestrator = new SyncOrchestrator(gmailService, geminiService);
        var jobId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await orchestrator.ExecuteSyncAsync(jobId, userId, progressReporter);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExecuteSyncAsync_BatchesEmailsAndDeduplicates()
    {
        var gmailService = Substitute.For<IGmailService>();
        var geminiService = Substitute.For<IGeminiService>();
        var progressReporter = Substitute.For<ISyncProgressReporter>();

        // 25 emails = 2 batches (20 + 5)
        var emails = Enumerable.Range(1, 25)
            .Select(i => new EmailMessage { Subject = $"Email {i}", Body = "body", From = "test@test.com", Date = DateTime.UtcNow })
            .ToList();

        gmailService.FetchEmailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(emails);

        var batchResult = new List<JobApplication>
        {
            new() { CompanyName = "Company A", JobRole = "Dev", AppliedDate = "01-04-2026", Status = "applied" }
        };

        geminiService.ClassifyBatchAsync(Arg.Any<List<EmailMessage>>(), Arg.Any<CancellationToken>())
            .Returns(batchResult);

        geminiService.DeduplicateAsync(Arg.Any<List<JobApplication>>(), Arg.Any<CancellationToken>())
            .Returns(batchResult);

        var orchestrator = new SyncOrchestrator(gmailService, geminiService);

        var result = await orchestrator.ExecuteSyncAsync(Guid.NewGuid(), Guid.NewGuid(), progressReporter);

        Assert.Single(result);
        Assert.Equal("Company A", result[0].CompanyName);

        // Should have been called twice (2 batches of 20 and 5)
        await geminiService.Received(2).ClassifyBatchAsync(Arg.Any<List<EmailMessage>>(), Arg.Any<CancellationToken>());
        // Final deduplicate pass
        await geminiService.Received(1).DeduplicateAsync(Arg.Any<List<JobApplication>>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/infrastructure.Tests --filter "SyncOrchestratorTests"
```

Expected: FAIL — `SyncOrchestrator` not found.

- [ ] **Step 3: Implement SyncOrchestrator**

```csharp
// infrastructure/Services/SyncOrchestrator.cs
using core.Interfaces;
using core.Models;

namespace infrastructure.Services;

public class SyncOrchestrator : ISyncOrchestrator
{
    private readonly IGmailService _gmailService;
    private readonly IGeminiService _geminiService;
    private const int BatchSize = 20;

    public SyncOrchestrator(IGmailService gmailService, IGeminiService geminiService)
    {
        _gmailService = gmailService;
        _geminiService = geminiService;
    }

    public async Task<List<JobApplication>> ExecuteSyncAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var emails = await _gmailService.FetchEmailsAsync(userId, cancellationToken);

        if (emails.Count == 0)
            return new List<JobApplication>();

        var allApplications = new List<JobApplication>();

        var batches = emails.Chunk(BatchSize).ToList();

        foreach (var batch in batches)
        {
            var batchResults = await _geminiService.ClassifyBatchAsync(batch.ToList(), cancellationToken);
            allApplications.AddRange(batchResults);
        }

        if (allApplications.Count == 0)
            return new List<JobApplication>();

        var deduplicated = await _geminiService.DeduplicateAsync(allApplications, cancellationToken);
        return deduplicated;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/infrastructure.Tests --filter "SyncOrchestratorTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement sync orchestrator with batching and deduplication"
```

---

## Task 9: SyncJobChannel & Background Worker ✅ COMPLETED

> **Previous:** Polling loop with sequential processing. **New:** Event-driven Channel<T> with concurrent per-job Tasks.
> ISyncJobChannel interface in core, SyncJobChannel implementation in infrastructure.
> SyncBackgroundService reads from channel, spawns a Task per job with its own DI scope.
> On startup, recovers orphaned Pending/Processing jobs from DB.

**Files:**

- Created: `core/Interfaces/ISyncJobChannel.cs`
- Created: `infrastructure/Services/SyncJobChannel.cs`
- Rewritten: `worker/SyncBackgroundService.cs`

- [x] **Step 1: Create ISyncJobChannel interface**

```csharp
// core/Interfaces/ISyncJobChannel.cs
namespace core.Interfaces;

public interface ISyncJobChannel
{
    ValueTask WriteAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken = default);
}
```

- [x] **Step 2: Create SyncJobChannel implementation**

```csharp
// infrastructure/Services/SyncJobChannel.cs
using System.Threading.Channels;
using core.Interfaces;

namespace infrastructure.Services;

public class SyncJobChannel : ISyncJobChannel
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ValueTask WriteAsync(Guid jobId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
```

- [x] **Step 3: Implement SyncBackgroundService with channel + concurrent processing**

```csharp
// worker/SyncBackgroundService.cs
using System.Text.Json;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace worker;

public class SyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobChannel _channel;
    private readonly ILogger<SyncBackgroundService> _logger;

    public SyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ISyncJobChannel channel,
        ILogger<SyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOrphanedJobsAsync(stoppingToken);

        await foreach (var jobId in _channel.ReadAllAsync(stoppingToken))
        {
            _ = ProcessJobAsync(jobId, stoppingToken);
        }
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orphanedIds = await dbContext.SyncJobs
            .Where(j => j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Processing)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in orphanedIds)
        {
            _logger.LogInformation("Recovering orphaned job {JobId}", id);
            await _channel.WriteAsync(id, cancellationToken);
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncOrchestrator>();
            var progressReporter = scope.ServiceProvider.GetRequiredService<ISyncProgressReporter>();

            var job = await dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job is null || job.Status != SyncJobStatus.Pending)
                return;

            job.Status = SyncJobStatus.Processing;
            await dbContext.SaveChangesAsync(cancellationToken);

            var results = await orchestrator.ExecuteSyncAsync(job.Id, job.UserId, progressReporter, cancellationToken);

            job.Result = JsonSerializer.SerializeToDocument(results);
            job.Status = SyncJobStatus.Completed;
            await dbContext.SaveChangesAsync(cancellationToken);

            await progressReporter.ReportCompletedAsync(job.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync job {JobId} failed", jobId);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var progressReporter = scope.ServiceProvider.GetRequiredService<ISyncProgressReporter>();

                var job = await dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
                if (job is not null)
                {
                    job.Status = SyncJobStatus.Failed;
                    job.Error = ex.Message;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await progressReporter.ReportFailedAsync(jobId, ex.Message, cancellationToken);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to update error status for job {JobId}", jobId);
            }
        }
    }
}
```

- [x] **Step 4: Verify build**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement channel-based concurrent background worker"
```

---

## Task 10: Mail Connect Controller ✅ COMPLETED

> Renamed from AuthController to MailConnectController — no auth implementation yet,
> endpoint is specific to Gmail connection for extensibility.
> Route: `api/mail-connect` with `gmail/url` and `gmail/connect` sub-routes.
> Request DTO `GmailConnectRequest` lives in `api-contracts/Requests/`.
> Added `api-contracts` classlib project for API request/response DTOs.
> `Google.Apis.Gmail.v1` package added to web-api.
> `AddControllers()` and `MapControllers()` wired in Program.cs.

**Files:**

- Created: `web-api/Controllers/MailConnectController.cs`
- Created: `api-contracts/Requests/GmailConnectRequest.cs`
- Created: `api-contracts/api-contracts.csproj` (new project)
- Tests not yet created

- [ ] **Step 1: Write failing test**

```csharp
// tests/web-api.tests/Controllers/MailConnectControllerTests.cs
using web_api.Controllers;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace web_api.Tests.Controllers;

public class MailConnectControllerTests
{
    [Fact]
    public void GetGmailUrl_ReturnsOkWithUrl()
    {
        var config = Substitute.For<IConfiguration>();
        config["Google:ClientId"].Returns("test-client-id");
        config["Google:RedirectUri"].Returns("http://localhost:3000/callback");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var controller = new MailConnectController(context, config);

        var result = controller.GetGmailUrl();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GmailConnect_CreatesUser_ReturnsUserId()
    {
        var config = Substitute.For<IConfiguration>();
        config["Google:ClientId"].Returns("test-client-id");
        config["Google:ClientSecret"].Returns("test-secret");
        config["Google:RedirectUri"].Returns("http://localhost:3000/callback");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var controller = new MailConnectController(context, config);

        // Note: Cannot fully integration-test OAuth exchange without mocking Google
        // This test verifies controller instantiation and route setup
        Assert.NotNull(controller);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/web_api.Tests --filter "MailConnectControllerTests"
```

Expected: FAIL — `MailConnectController` not found.

- [ ] **Step 3: Implement MailConnectController**

```csharp
// web-api/Controllers/MailConnectController.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using core.Entities;
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

    public MailConnectController(AppDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
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
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _configuration["Google:ClientId"]!,
                ClientSecret = _configuration["Google:ClientSecret"]!
            },
            Scopes = [GmailService.Scope.GmailReadonly]
        });

        var tokenResponse = await flow.ExchangeCodeForTokenAsync(
            "user",
            request.Code,
            _configuration["Google:RedirectUri"]!,
            CancellationToken.None);

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            TokenExpiresAt = tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600)
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return Ok(new { userId = user.Id });
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/web_api.Tests --filter "MailConnectControllerTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement mail connect controller with Gmail OAuth connect"
```

---

## Task 11: Sync Controller ✅ COMPLETED

> **Previous:** Creates pending job and returns jobId. **New:** Also rejects duplicate jobs (409 Conflict) and writes jobId to SyncJobChannel for immediate processing.
> `StartSyncRequest` DTO lives in `api-contracts/Requests/`.
> GetStatus response already includes `progress` and `stage` fields (Task 18 scope covered here).

**Files:**

- Modified: `web-api/Controllers/SyncController.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/web-api.tests/Controllers/SyncControllerTests.cs
using System.Text.Json;
using web_api.Controllers;
using core.Entities;
using core.Enums;
using core.Models;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace web_api.Tests.Controllers;

public class SyncControllerTests
{
    [Fact]
    public async Task StartSync_CreatesJob_ReturnsJobId()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var user = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = "User", AccessToken = "a", RefreshToken = "r", TokenExpiresAt = DateTime.UtcNow.AddHours(1) };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var controller = new SyncController(context);

        var result = await controller.StartSync(new StartSyncRequest { UserId = user.Id });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetStatus_ReturnsJobWithResults()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var applications = new List<JobApplication>
        {
            new() { CompanyName = "TestCo", JobRole = "Dev", AppliedDate = "2026-04-01", Status = "applied" }
        };

        var job = new SyncJob
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Status = SyncJobStatus.Completed,
            Result = JsonSerializer.SerializeToDocument(applications)
        };
        context.SyncJobs.Add(job);
        await context.SaveChangesAsync();

        var controller = new SyncController(context);

        var result = await controller.GetStatus(job.Id);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetStatus_ReturnsNotFound_WhenJobMissing()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var controller = new SyncController(context);

        var result = await controller.GetStatus(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/web_api.Tests --filter "SyncControllerTests"
```

Expected: FAIL — `SyncController` not found.

- [x] **Step 3: Update SyncController with 409 Conflict + channel write**

```csharp
// web-api/Controllers/SyncController.cs
using api_contracts.Requests;
using core.Entities;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace web_api.Controllers;

[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ISyncJobChannel _syncJobChannel;

    public SyncController(AppDbContext dbContext, ISyncJobChannel syncJobChannel)
    {
        _dbContext = dbContext;
        _syncJobChannel = syncJobChannel;
    }

    [HttpPost]
    public async Task<IActionResult> StartSync([FromBody] StartSyncRequest request)
    {
        var userExists = await _dbContext.Users.AnyAsync(u => u.Id == request.UserId);
        if (!userExists)
            return BadRequest(new { error = "User not found" });

        var hasActiveJob = await _dbContext.SyncJobs.AnyAsync(j =>
            j.UserId == request.UserId &&
            (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Processing));
        if (hasActiveJob)
            return Conflict(new { error = "A sync job is already in progress for this user" });

        var job = new SyncJob
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Status = SyncJobStatus.Pending
        };

        _dbContext.SyncJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await _syncJobChannel.WriteAsync(job.Id);

        return Ok(new { jobId = job.Id });
    }

    [HttpGet("status/{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId)
    {
        var job = await _dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job is null)
            return NotFound();

        return Ok(new
        {
            jobId = job.Id,
            status = job.Status.ToString().ToLowerInvariant(),
            progress = job.Progress,
            stage = job.Stage,
            result = job.Result,
            error = job.Error
        });
    }
}
```

- [x] **Step 4: Verify build**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add 409 conflict check and channel dispatch to sync controller"
```

---

## Task 12: Configuration Files ✅ COMPLETED

> Google config section (ClientId, ClientSecret, RedirectUri, GeminiApiKey) added to appsettings.json.
> ConnectionString placeholder already present from Task 5.

**Files:**

- Modify: `web-api/appsettings.json`

- [x] **Step 1: Configure appsettings.json**

```json
// web-api/appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=jobsync;Username=postgres;Password=postgres"
  },
  "Google": {
    "ClientId": "",
    "ClientSecret": "",
    "RedirectUri": "http://localhost:3000/callback",
    "GeminiApiKey": ""
  }
}
```

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "feat: add application configuration"
```

---

## Task 13: Initial Program.cs (without SignalR — added in Task 17) ✅ COMPLETED

> Controllers, EF Core, SignalR, CORS, all service DI registrations, and hosted service wired.
> Combined with Task 17 — final Program.cs has full setup.

- [ ] **Step 1: Configure basic Program.cs**

```csharp
// web-api/Program.cs
using core.Interfaces;
using infrastructure.Data;
using infrastructure.Services;
using worker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IGmailService, GmailService>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<ISyncOrchestrator, SyncOrchestrator>();
builder.Services.AddHostedService<SyncBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();

app.Run();
```

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: wire up DI, EF Core, and background service in Program.cs"
```

---

## Task 14: SignalR Hub & Progress Reporting ✅ COMPLETED

> SyncHub created with JoinJob/LeaveJob group management.
> SignalR wired in Program.cs (`AddSignalR()` + `MapHub<SyncHub>("/hubs/sync")`).
> ISyncProgressReporter interface exists (created in Task 3).
> ISyncHubNotifier abstraction created in core to avoid circular dependency (infrastructure → web-api).
> SyncProgressReporter implemented in infrastructure — persists progress to DB + delegates to ISyncHubNotifier.
> SyncHubNotifier implemented in web-api/Services — uses IHubContext<SyncHub> to push SignalR events.

**Files:**

- Created: `web-api/Hubs/SyncHub.cs`
- Created: `core/Interfaces/ISyncHubNotifier.cs`
- Created: `core/Interfaces/ISyncProgressReporter.cs` (from Task 3)
- Created: `infrastructure/Services/SyncProgressReporter.cs`
- Created: `web-api/Services/SyncHubNotifier.cs`

- [x] **Step 1: Create ISyncProgressReporter interface**

```csharp
// core/Interfaces/ISyncProgressReporter.cs
namespace core.Interfaces;

public interface ISyncProgressReporter
{
    Task ReportProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default);
    Task ReportCompletedAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task ReportFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default);
}
```

- [x] **Step 2: Create SyncHub**

```csharp
// web-api/Hubs/SyncHub.cs
using Microsoft.AspNetCore.SignalR;

namespace web_api.Hubs;

public class SyncHub : Hub
{
    public async Task JoinJob(Guid jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"sync-{jobId}");
    }

    public async Task LeaveJob(Guid jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sync-{jobId}");
    }
}
```

- [x] **Step 3: Create SyncProgressReporter**

```csharp
// infrastructure/Services/SyncProgressReporter.cs
using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace infrastructure.Services;

public class SyncProgressReporter : ISyncProgressReporter
{
    private readonly AppDbContext _dbContext;
    private readonly ISyncHubNotifier _hubNotifier;

    public SyncProgressReporter(AppDbContext dbContext, ISyncHubNotifier hubNotifier)
    {
        _dbContext = dbContext;
        _hubNotifier = hubNotifier;
    }

    public async Task ReportProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job != null)
        {
            job.Stage = stage;
            job.Progress = percent;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _hubNotifier.SendProgressAsync(jobId, stage, percent, cancellationToken);
    }

    public async Task ReportCompletedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await _hubNotifier.SendCompletedAsync(jobId, cancellationToken);
    }

    public async Task ReportFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        await _hubNotifier.SendFailedAsync(jobId, error, cancellationToken);
    }
}
```

- [x] **Step 4: Create ISyncHubNotifier and SyncHubNotifier**

```csharp
// core/Interfaces/ISyncHubNotifier.cs
namespace core.Interfaces;

public interface ISyncHubNotifier
{
    Task SendProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default);
    Task SendCompletedAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task SendFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default);
}
```

```csharp
// web-api/Services/SyncHubNotifier.cs
using core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using web_api.Hubs;

namespace web_api.Services;

public class SyncHubNotifier : ISyncHubNotifier
{
    private readonly IHubContext<SyncHub> _hubContext;

    public SyncHubNotifier(IHubContext<SyncHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendProgressAsync(Guid jobId, string stage, int percent, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"sync-{jobId}")
            .SendAsync("SyncProgress", stage, percent, cancellationToken);
    }

    public async Task SendCompletedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"sync-{jobId}")
            .SendAsync("SyncCompleted", cancellationToken);
    }

    public async Task SendFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"sync-{jobId}")
            .SendAsync("SyncFailed", error, cancellationToken);
    }
}
```

- [x] **Step 5: Verify build**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add SignalR hub and progress reporter"
```

---

## Task 15: Update Orchestrator with Progress Reporting ✅ COMPLETED

> ISyncOrchestrator interface has the updated signature (jobId, userId, progressReporter).
> SyncOrchestrator implemented with progress reporting at each stage:
> Fetching emails (5%), Processing batch N/M (10-90%), Deduplicating (90%), Done (100%).

**Files:**

- Modify: `core/Interfaces/ISyncOrchestrator.cs`
- Modify: `infrastructure/Services/SyncOrchestrator.cs`

- [x] **Step 1: Update ISyncOrchestrator to accept progress reporter and jobId**

```csharp
// core/Interfaces/ISyncOrchestrator.cs
using core.Models;

namespace core.Interfaces;

public interface ISyncOrchestrator
{
    Task<List<JobApplication>> ExecuteSyncAsync(Guid jobId, Guid userId, ISyncProgressReporter progressReporter, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Update SyncOrchestrator to emit progress**

```csharp
// infrastructure/Services/SyncOrchestrator.cs
using core.Interfaces;
using core.Models;

namespace infrastructure.Services;

public class SyncOrchestrator : ISyncOrchestrator
{
    private readonly IGmailService _gmailService;
    private readonly IGeminiService _geminiService;
    private const int BatchSize = 20;

    public SyncOrchestrator(IGmailService gmailService, IGeminiService geminiService)
    {
        _gmailService = gmailService;
        _geminiService = geminiService;
    }

    public async Task<List<JobApplication>> ExecuteSyncAsync(Guid jobId, Guid userId, ISyncProgressReporter progressReporter, CancellationToken cancellationToken = default)
    {
        await progressReporter.ReportProgressAsync(jobId, "Fetching emails", 5, cancellationToken);

        var emails = await _gmailService.FetchEmailsAsync(userId, cancellationToken);

        if (emails.Count == 0)
            return new List<JobApplication>();

        var allApplications = new List<JobApplication>();
        var batches = emails.Chunk(BatchSize).ToList();
        var totalBatches = batches.Count;

        for (var i = 0; i < totalBatches; i++)
        {
            var percent = 10 + (int)((80.0 / totalBatches) * i);
            await progressReporter.ReportProgressAsync(jobId, $"Processing batch {i + 1}/{totalBatches}", percent, cancellationToken);

            var batchResults = await _geminiService.ClassifyBatchAsync(batches[i].ToList(), cancellationToken);
            allApplications.AddRange(batchResults);
        }

        if (allApplications.Count == 0)
            return new List<JobApplication>();

        await progressReporter.ReportProgressAsync(jobId, "Deduplicating results", 90, cancellationToken);

        var deduplicated = await _geminiService.DeduplicateAsync(allApplications, cancellationToken);

        await progressReporter.ReportProgressAsync(jobId, "Done", 100, cancellationToken);

        return deduplicated;
    }
}
```

- [ ] **Step 3: Update SyncOrchestrator tests**

```csharp
// tests/infrastructure.tests/Services/SyncOrchestratorTests.cs
using core.Interfaces;
using core.Models;
using infrastructure.Services;
using NSubstitute;

namespace infrastructure.Tests.Services;

public class SyncOrchestratorTests
{
    [Fact]
    public async Task ExecuteSyncAsync_ReturnsEmpty_WhenNoEmails()
    {
        var gmailService = Substitute.For<IGmailService>();
        var geminiService = Substitute.For<IGeminiService>();
        var progressReporter = Substitute.For<ISyncProgressReporter>();

        gmailService.FetchEmailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmailMessage>());

        var orchestrator = new SyncOrchestrator(gmailService, geminiService);
        var jobId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await orchestrator.ExecuteSyncAsync(jobId, userId, progressReporter);

        Assert.Empty(result);
        await progressReporter.Received(1).ReportProgressAsync(jobId, "Fetching emails", 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSyncAsync_BatchesEmailsAndReportsProgress()
    {
        var gmailService = Substitute.For<IGmailService>();
        var geminiService = Substitute.For<IGeminiService>();
        var progressReporter = Substitute.For<ISyncProgressReporter>();

        var emails = Enumerable.Range(1, 25)
            .Select(i => new EmailMessage { Subject = $"Email {i}", Body = "body", From = "test@test.com", Date = DateTime.UtcNow })
            .ToList();

        gmailService.FetchEmailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(emails);

        var batchResult = new List<JobApplication>
        {
            new() { CompanyName = "Company A", JobRole = "Dev", AppliedDate = "2026-04-01", Status = "applied" }
        };

        geminiService.ClassifyBatchAsync(Arg.Any<List<EmailMessage>>(), Arg.Any<CancellationToken>())
            .Returns(batchResult);

        geminiService.DeduplicateAsync(Arg.Any<List<JobApplication>>(), Arg.Any<CancellationToken>())
            .Returns(batchResult);

        var orchestrator = new SyncOrchestrator(gmailService, geminiService);
        var jobId = Guid.NewGuid();

        var result = await orchestrator.ExecuteSyncAsync(jobId, Guid.NewGuid(), progressReporter);

        Assert.Single(result);

        // Verify progress was reported: fetch + 2 batches + deduplicate + done
        await progressReporter.Received(5).ReportProgressAsync(jobId, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/infrastructure.Tests --filter "SyncOrchestratorTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add progress reporting to sync orchestrator"
```

---

## Task 16: ~~Update Background Worker with SignalR~~ SUPERSEDED

> Superseded by Task 9 rework. The new Channel<T>-based SyncBackgroundService already includes
> progress reporting and SignalR integration via ISyncProgressReporter.

---

## Task 17: Update Program.cs with SignalR ✅ COMPLETED

> Full Program.cs with all DI registrations: IGmailService, IGeminiService, ISyncOrchestrator,
> ISyncProgressReporter, ISyncHubNotifier, SyncBackgroundService hosted service.
> SignalR, CORS, OpenApi all wired. Combined with Task 13.

**Files:**

- Modify: `web-api/Program.cs`

- [ ] **Step 1: Update Program.cs to register SignalR and progress reporter**

```csharp
// web-api/Program.cs
using web_api.Hubs;
using web_api.Services;
using core.Interfaces;
using infrastructure.Data;
using infrastructure.Services;
using worker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IGmailService, GmailService>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<ISyncOrchestrator, SyncOrchestrator>();
builder.Services.AddScoped<ISyncProgressReporter, SyncProgressReporter>();
builder.Services.AddScoped<ISyncHubNotifier, SyncHubNotifier>();
builder.Services.AddSingleton<ISyncJobChannel, SyncJobChannel>();
builder.Services.AddHostedService<SyncBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync");

app.Run();
```

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: register SignalR hub and progress reporter in DI"
```

---

## Task 18: ~~Update SyncController with Progress Fields~~ SUPERSEDED

> Already included `stage` and `progress` in the GetStatus response
> when SyncController was implemented in Task 11.

**Files:**

- Modify: `web-api/Controllers/SyncController.cs`

- [ ] **Step 1: Update GetStatus to include stage and percent**

```csharp
    [HttpGet("status/{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId)
    {
        var job = await _dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null)
            return NotFound();

        return Ok(new
        {
            jobId = job.Id,
            status = job.Status.ToString().ToLowerInvariant(),
            progress = job.Progress,
            stage = job.Stage,
            result = job.Result,
            error = job.Error
        });
    }
```

- [ ] **Step 2: Run tests**

```bash
dotnet test tests/web_api.Tests --filter "SyncControllerTests"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: include stage and percent in sync status response"
```

---

## Task 19: Initial EF Migration ✅ COMPLETED

> Migration `20260510042549_InitialCreate` created and applied in Task 5.

**Files:**

- Create: `infrastructure/Data/Migrations/` (auto-generated)

- [x] **Step 1: Create initial migration**

```bash
dotnet ef migrations add InitialCreate --project infrastructure --startup-project web-api --output-dir Data/Migrations
```

- [x] **Step 2: Verify migration generated**

```bash
ls infrastructure/Data/Migrations/
```

Expected: Files like `*_InitialCreate.cs` and `AppDbContextModelSnapshot.cs`.

- [x] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add initial EF Core migration"
```

---

## Task 20: Final Integration Verification

- [ ] **Step 1: Run all tests**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 2: Verify app starts (requires PostgreSQL running)**

```bash
cd web-api
dotnet run &
sleep 3
curl http://localhost:5000/swagger/index.html -s -o /dev/null -w "%{http_code}"
kill %1
```

Expected: HTTP 200 from Swagger.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "chore: verify full integration build and test pass"
```
