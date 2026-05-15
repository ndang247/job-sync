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
    public async Task StartSync_ValidConnection_ReturnsOkWithJobId()
    {
        var (userId, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = content.GetProperty("jobId").GetString();
        Assert.NotNull(jobId);
        Assert.True(Guid.TryParse(jobId, out _));
    }

    [Fact]
    public async Task StartSync_ValidConnection_PersistsJobInDb()
    {
        var (userId, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        using var db = _factory.CreateDbContext();
        var job = await db.SyncJobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(userId, job.UserId);
        Assert.Equal(connId, job.EmailConnectionId);
        Assert.Equal(SyncJobStatus.Pending, job.Status);
    }

    [Fact]
    public async Task StartSync_ValidConnection_WritesToChannel()
    {
        var (userId, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        await _factory.MockSyncJobChannel.Received(1)
            .WriteAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSync_NonExistentUser_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId = Guid.NewGuid(), emailConnectionId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("User not found", content.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StartSync_NoConnection_ReturnsConflictWithGrantCode()
    {
        Guid userId;
        using (var db = _factory.CreateDbContext())
        {
            var user = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = "User" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CONNECTION_REQUIRES_GRANT", content.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartSync_ConnectionNotOwnedByUser_ReturnsConflict()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        // Create different user
        Guid otherUserId;
        using (var db = _factory.CreateDbContext())
        {
            var other = new User { Id = Guid.NewGuid(), FirstName = "Other", LastName = "Person" };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            otherUserId = other.Id;
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId = otherUserId, emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CONNECTION_REQUIRES_GRANT", content.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartSync_NeedsReconnectConnection_ReturnsConflict()
    {
        Guid userId;
        Guid connId;
        using (var db = _factory.CreateDbContext())
        {
            var user = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = "User" };
            db.Users.Add(user);
            var conn = new EmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = "test@gmail.com",
                SubjectId = "sub-needs-reconnect",
                RefreshToken = "rt",
                GrantedScopes = "gmail.readonly",
                Status = EmailConnectionStatus.NeedsReconnect
            };
            db.EmailConnections.Add(conn);
            await db.SaveChangesAsync();
            userId = user.Id;
            connId = conn.Id;
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CONNECTION_REQUIRES_GRANT", content.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartSync_DuplicateActiveJob_ReturnsConflict()
    {
        var (userId, connId) = await SeedUserWithConnectionAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EmailConnectionId = connId,
                Status = SyncJobStatus.Pending
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("already in progress", content.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StartSync_ProcessingJob_ReturnsConflict()
    {
        var (userId, connId) = await SeedUserWithConnectionAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EmailConnectionId = connId,
                Status = SyncJobStatus.Processing
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StartSync_CompletedJobExists_AllowsNewSync()
    {
        var (userId, connId) = await SeedUserWithConnectionAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EmailConnectionId = connId,
                Status = SyncJobStatus.Completed
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartSync_FailedJobExists_AllowsNewSync()
    {
        var (userId, connId) = await SeedUserWithConnectionAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EmailConnectionId = connId,
                Status = SyncJobStatus.Failed,
                Error = "Previous failure"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartSync_DifferentConnections_AllowsConcurrentJobs()
    {
        var (userId, connId1) = await SeedUserWithConnectionAsync();

        // Add second connection for same user
        Guid connId2;
        using (var db = _factory.CreateDbContext())
        {
            var conn2 = new EmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Email = "second@gmail.com",
                SubjectId = "sub-second-" + Guid.NewGuid(),
                RefreshToken = "rt2",
                GrantedScopes = "gmail.readonly",
                Status = EmailConnectionStatus.Active
            };
            db.EmailConnections.Add(conn2);
            await db.SaveChangesAsync();
            connId2 = conn2.Id;
        }

        // Start sync for first connection
        var response1 = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId1 });
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Start sync for second connection (should also succeed)
        var response2 = await _client.PostAsJsonAsync("/api/v1/sync", new { userId, emailConnectionId = connId2 });
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    [Fact]
    public async Task GetStatus_ExistingJob_ReturnsJobDetails()
    {
        var jobId = Guid.NewGuid();
        var (userId, connId) = await SeedUserWithConnectionAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = jobId,
                UserId = userId,
                EmailConnectionId = connId,
                Status = SyncJobStatus.Processing,
                Progress = 45,
                Stage = "Processing batch 2/4"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/v1/sync/status/{jobId}");

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
        var (userId, connId) = await SeedUserWithConnectionAsync();
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
                UserId = userId,
                EmailConnectionId = connId,
                Status = SyncJobStatus.Completed,
                Progress = 100,
                Stage = "Done",
                Result = JsonSerializer.SerializeToDocument(applications)
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/v1/sync/status/{jobId}");

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
        var (userId, connId) = await SeedUserWithConnectionAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = jobId,
                UserId = userId,
                EmailConnectionId = connId,
                Status = SyncJobStatus.Failed,
                Error = "Gmail token expired"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/v1/sync/status/{jobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", content.GetProperty("status").GetString());
        Assert.Equal("Gmail token expired", content.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetStatus_NonExistentJob_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/v1/sync/status/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_InvalidGuid_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/v1/sync/status/not-a-guid");

        // Route constraint {jobId:guid} should reject this
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid UserId, Guid ConnectionId)> SeedUserWithConnectionAsync()
    {
        using var db = _factory.CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
        };
        db.Users.Add(user);

        var connection = new EmailConnection
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Email = $"test-{Guid.NewGuid()}@gmail.com",
            SubjectId = $"sub-{Guid.NewGuid()}",
            RefreshToken = "test-refresh-token",
            GrantedScopes = "gmail.readonly",
            Status = EmailConnectionStatus.Active
        };
        db.EmailConnections.Add(connection);

        await db.SaveChangesAsync();
        return (user.Id, connection.Id);
    }
}
