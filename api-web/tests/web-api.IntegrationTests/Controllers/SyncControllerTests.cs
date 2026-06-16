using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using core.Entities;
using core.Enums;
using NSubstitute;

namespace web_api.IntegrationTests.Controllers;

public class SyncControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _userId = Guid.NewGuid();

    public SyncControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = _userId,
                UserName = $"sync-{_userId:N}@example.com",
                Email = $"sync-{_userId:N}@example.com",
                FirstName = "Test",
                LastName = "User"
            });
            db.SaveChanges();
        }
        _client = factory.CreateAuthenticatedClient(_userId);
    }

    [Fact]
    public async Task StartSync_ValidConnection_ReturnsOkWithJobId()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });

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

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        using var db = _factory.CreateDbContext();
        var job = await db.SyncJobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(userId, job.UserId);
        Assert.Equal(connId, job.EmailConnectionId);
        Assert.Equal(SyncJobStatus.Pending, job.Status);
        Assert.True(job.SyncStartUtc < job.SyncEndUtcExclusive);
        Assert.Equal(TimeZoneInfo.Local.Id, job.SyncTimeZone);
    }

    [Fact]
    public async Task StartSync_MissingDateRange_DefaultsToToday()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        using var db = _factory.CreateDbContext();
        var job = await db.SyncJobs.FindAsync(jobId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(job);

        var localStartDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(job.SyncStartUtc, DateTimeKind.Utc), TimeZoneInfo.Local));
        var localEndDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(job.SyncEndUtcExclusive, DateTimeKind.Utc), TimeZoneInfo.Local).AddTicks(-1));

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), localStartDate);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), localEndDate);
        Assert.Equal(TimeZoneInfo.Local.Id, job.SyncTimeZone);
    }

    [Fact]
    public async Task StartSync_SingleDateRange_PersistsWholeDayUtcWindow()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new
        {
            emailConnectionId = connId,
            dateRange = new
            {
                startDate = "2026-06-16",
                timeZone = "Australia/Sydney"
            }
        });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        using var db = _factory.CreateDbContext();
        var job = await db.SyncJobs.FindAsync(jobId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(job);
        Assert.Equal(new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc), job.SyncStartUtc);
        Assert.Equal(new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc), job.SyncEndUtcExclusive);
        Assert.Equal("Australia/Sydney", job.SyncTimeZone);
    }

    [Fact]
    public async Task StartSync_DateRange_PersistsInclusiveEndDateUtcWindow()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new
        {
            emailConnectionId = connId,
            dateRange = new
            {
                startDate = "2026-06-10",
                endDate = "2026-06-16",
                timeZone = "Australia/Sydney"
            }
        });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        using var db = _factory.CreateDbContext();
        var job = await db.SyncJobs.FindAsync(jobId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(job);
        Assert.Equal(new DateTime(2026, 6, 9, 14, 0, 0, DateTimeKind.Utc), job.SyncStartUtc);
        Assert.Equal(new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc), job.SyncEndUtcExclusive);
        Assert.Equal("Australia/Sydney", job.SyncTimeZone);
    }

    [Fact]
    public async Task StartSync_EndDateBeforeStartDate_ReturnsBadRequest()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new
        {
            emailConnectionId = connId,
            dateRange = new
            {
                startDate = "2026-06-16",
                endDate = "2026-06-10",
                timeZone = "Australia/Sydney"
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_SYNC_DATE_RANGE", content.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartSync_InvalidStartDate_ReturnsBadRequest()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new
        {
            emailConnectionId = connId,
            dateRange = new
            {
                startDate = "16-06-2026",
                timeZone = "Australia/Sydney"
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_SYNC_DATE_RANGE", content.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartSync_InvalidTimeZone_ReturnsBadRequest()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new
        {
            emailConnectionId = connId,
            dateRange = new
            {
                startDate = "2026-06-16",
                timeZone = "Mars/Olympus"
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_SYNC_DATE_RANGE", content.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartSync_ValidConnection_WritesToChannel()
    {
        var (_, connId) = await SeedUserWithConnectionAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(content.GetProperty("jobId").GetString()!);

        await _factory.MockSyncJobChannel.Received(1)
            .WriteAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSync_NoConnection_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartSync_ConnectionOwnedByAnotherUser_ReturnsNotFound()
    {
        Guid connId;
        using (var db = _factory.CreateDbContext())
        {
            var conn = new EmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = Guid.Empty,
                Email = "orphan@gmail.com",
                SubjectId = $"sub-orphan-{Guid.NewGuid()}",
                RefreshToken = "rt",
                GrantedScopes = "gmail.readonly",
                Status = EmailConnectionStatus.Active
            };
            db.EmailConnections.Add(conn);
            await db.SaveChangesAsync();
            connId = conn.Id;
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartSync_NeedsReconnectConnection_ReturnsConflict()
    {
        Guid connId;
        using (var db = _factory.CreateDbContext())
        {
            var conn = new EmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                Email = "test@gmail.com",
                SubjectId = "sub-needs-reconnect",
                RefreshToken = "rt",
                GrantedScopes = "gmail.readonly",
                Status = EmailConnectionStatus.NeedsReconnect
            };
            db.EmailConnections.Add(conn);
            await db.SaveChangesAsync();
            connId = conn.Id;
        }

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });

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

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });

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

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });

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

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });

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

        var response = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId });

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
        var response1 = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId1 });
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Start sync for second connection (should also succeed)
        var response2 = await _client.PostAsJsonAsync("/api/v1/sync", new { emailConnectionId = connId2 });
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
            new() { CompanyName = "Acme Corp", JobRole = "Engineer", AppliedDate = "10-05-2026", Status = JobApplicationStatus.Applied },
            new() { CompanyName = "Globex", JobRole = "Dev", AppliedDate = "09-05-2026", Status = JobApplicationStatus.Applied }
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
        var connection = new EmailConnection
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Email = $"test-{Guid.NewGuid()}@gmail.com",
            SubjectId = $"sub-{Guid.NewGuid()}",
            RefreshToken = "test-refresh-token",
            GrantedScopes = "gmail.readonly",
            Status = EmailConnectionStatus.Active
        };
        db.EmailConnections.Add(connection);

        await db.SaveChangesAsync();
        return (_userId, connection.Id);
    }
}
