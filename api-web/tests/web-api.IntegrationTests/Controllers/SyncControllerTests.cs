using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using core.Entities;
using core.Enums;
using core.Models;
using NSubstitute;

namespace web_api.IntegrationTests.Controllers;

public class SyncControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public SyncControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StartSync_ValidUser_ReturnsOkWithJobId()
    {
        var userId = await SeedUserAsync();

        var response = await _client.PostAsJsonAsync("/api/sync", new { userId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = content.GetProperty("jobId").GetString();
        Assert.NotNull(jobId);
        Assert.True(Guid.TryParse(jobId, out _));
    }

    [Fact]
    public async Task StartSync_ValidUser_PersistsJobInDb()
    {
        var userId = await SeedUserAsync();

        var response = await _client.PostAsJsonAsync("/api/sync", new { userId });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        using var db = _factory.CreateDbContext();
        var job = await db.SyncJobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(userId, job.UserId);
        Assert.Equal(SyncJobStatus.Pending, job.Status);
    }

    [Fact]
    public async Task StartSync_ValidUser_WritesToChannel()
    {
        var userId = await SeedUserAsync();

        var response = await _client.PostAsJsonAsync("/api/sync", new { userId });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        await _factory.MockSyncJobChannel.Received(1)
            .WriteAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSync_NonExistentUser_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/sync", new { userId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("User not found", content.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StartSync_DuplicateActiveJob_ReturnsConflict()
    {
        var userId = await SeedUserAsync();

        // Create a pending job directly in DB
        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = SyncJobStatus.Pending
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/sync", new { userId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("already in progress", content.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StartSync_ProcessingJob_ReturnsConflict()
    {
        var userId = await SeedUserAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = SyncJobStatus.Processing
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/sync", new { userId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StartSync_CompletedJobExists_AllowsNewSync()
    {
        var userId = await SeedUserAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = SyncJobStatus.Completed
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/sync", new { userId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartSync_FailedJobExists_AllowsNewSync()
    {
        var userId = await SeedUserAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = SyncJobStatus.Failed,
                Error = "Previous failure"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/sync", new { userId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_ExistingJob_ReturnsJobDetails()
    {
        var jobId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = jobId,
                UserId = userId,
                Status = SyncJobStatus.Processing,
                Progress = 45,
                Stage = "Processing batch 2/4"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/sync/status/{jobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId.ToString(), content.GetProperty("jobId").GetString());
        Assert.Equal("processing", content.GetProperty("status").GetString());
        Assert.Equal(45, content.GetProperty("progress").GetInt32());
        Assert.Equal("Processing batch 2/4", content.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task GetStatus_CompletedJob_ReturnsResults()
    {
        var jobId = Guid.NewGuid();
        var applications = new List<JobApplication>
        {
            new() { CompanyName = "Acme Corp", JobRole = "Engineer", AppliedDate = "10-05-2026", Status = "applied" },
            new() { CompanyName = "Globex", JobRole = "Dev", AppliedDate = "09-05-2026", Status = "applied" }
        };

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = jobId,
                UserId = Guid.NewGuid(),
                Status = SyncJobStatus.Completed,
                Progress = 100,
                Stage = "Done",
                Result = JsonSerializer.SerializeToDocument(applications)
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/sync/status/{jobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", content.GetProperty("status").GetString());
        Assert.Equal(100, content.GetProperty("progress").GetInt32());

        var result = content.GetProperty("result");
        Assert.Equal(2, result.GetArrayLength());
        Assert.Equal("Acme Corp", result[0].GetProperty("CompanyName").GetString());
    }

    [Fact]
    public async Task GetStatus_FailedJob_ReturnsError()
    {
        var jobId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = jobId,
                UserId = Guid.NewGuid(),
                Status = SyncJobStatus.Failed,
                Error = "Gmail token expired"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/sync/status/{jobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", content.GetProperty("status").GetString());
        Assert.Equal("Gmail token expired", content.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetStatus_NonExistentJob_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/sync/status/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_InvalidGuid_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/sync/status/not-a-guid");

        // Route constraint {jobId:guid} should reject this
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> SeedUserAsync()
    {
        using var db = _factory.CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
