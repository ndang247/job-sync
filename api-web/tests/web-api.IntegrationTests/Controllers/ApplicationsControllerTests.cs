using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using core.Entities;
using core.Enums;

namespace web_api.IntegrationTests.Controllers;

public class ApplicationsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public ApplicationsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_NoApplications_ReturnsOkWithArray()
    {
        var response = await _client.GetAsync("/api/v1/applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
    }

    [Fact]
    public async Task GetAll_WithApplications_ReturnsSeededApplications()
    {
        var connId = await SeedConnectionAsync();
        var msgId1 = $"msg-{Guid.NewGuid()}";
        var msgId2 = $"msg-{Guid.NewGuid()}";

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.AddRange(
                new JobApplication
                {
                    Id = Guid.NewGuid(),
                    CompanyName = "Acme Corp",
                    JobRole = "Software Engineer",
                    AppliedDate = "15-05-2026",
                    Status = JobApplicationStatus.Applied,
                    MessageId = msgId1,
                    EmailConnectionId = connId
                },
                new JobApplication
                {
                    Id = Guid.NewGuid(),
                    CompanyName = "Globex Inc",
                    JobRole = "Backend Developer",
                    AppliedDate = "14-05-2026",
                    Status = JobApplicationStatus.Applied,
                    MessageId = msgId2,
                    EmailConnectionId = connId
                });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var companies = Enumerable.Range(0, content.GetArrayLength())
            .Select(i => content[i].GetProperty("companyName").GetString())
            .ToList();
        Assert.Contains("Acme Corp", companies);
        Assert.Contains("Globex Inc", companies);
    }

    [Fact]
    public async Task GetAll_ReturnsCorrectShape()
    {
        var connId = await SeedConnectionAsync();
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                CompanyName = "TestCo",
                JobRole = "Fullstack Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/applications");
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = content[0];

        Assert.Equal("TestCo", item.GetProperty("companyName").GetString());
        Assert.Equal("Fullstack Dev", item.GetProperty("jobRole").GetString());
        Assert.Equal("20-05-2026", item.GetProperty("appliedDate").GetString());
        Assert.Equal("Applied", item.GetProperty("status").GetString());
        Assert.True(item.TryGetProperty("email", out _));
        Assert.True(item.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task GetAll_OrderedByCreatedAtDescending()
    {
        var connId = await SeedConnectionAsync();
        var olderName = $"OlderCo-{Guid.NewGuid()}";
        var newerName = $"NewerCo-{Guid.NewGuid()}";
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = olderId,
                CompanyName = olderName,
                JobRole = "Dev",
                AppliedDate = "10-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            db.JobApplications.Add(new JobApplication
            {
                Id = newerId,
                CompanyName = newerName,
                JobRole = "Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();

            // Update CreatedAt after insert to bypass SetTimestamps override
            var older = await db.JobApplications.FindAsync(olderId);
            var newer = await db.JobApplications.FindAsync(newerId);
            older!.CreatedAt = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
            newer!.CreatedAt = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/applications");
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();

        var companies = Enumerable.Range(0, content.GetArrayLength())
            .Select(i => content[i].GetProperty("companyName").GetString())
            .ToList();
        var olderIdx = companies.IndexOf(olderName);
        var newerIdx = companies.IndexOf(newerName);
        Assert.True(newerIdx < olderIdx, "Newer item should appear before older item");
    }

    [Fact]
    public async Task GetAll_ExcludesSoftDeleted()
    {
        var connId = await SeedConnectionAsync();
        var deletedMsg = $"msg-deleted-{Guid.NewGuid()}";
        var activeMsg = $"msg-active-{Guid.NewGuid()}";

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = Guid.NewGuid(),
                CompanyName = "DeletedCo",
                JobRole = "Dev",
                AppliedDate = "10-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = deletedMsg,
                EmailConnectionId = connId,
                DeletedAt = DateTime.UtcNow
            });
            db.JobApplications.Add(new JobApplication
            {
                Id = Guid.NewGuid(),
                CompanyName = "ActiveCo",
                JobRole = "Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = activeMsg,
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/applications");
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();

        var companies = Enumerable.Range(0, content.GetArrayLength())
            .Select(i => content[i].GetProperty("companyName").GetString())
            .ToList();
        Assert.Contains("ActiveCo", companies);
        Assert.DoesNotContain("DeletedCo", companies);
    }

    [Fact]
    public async Task GetAll_IncludesEmailFromConnection()
    {
        var connId = await SeedConnectionAsync("jobs@example.com");

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = Guid.NewGuid(),
                CompanyName = "EmailCo",
                JobRole = "Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/applications");
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("jobs@example.com", content[0].GetProperty("email").GetString());
    }

    private async Task<Guid> SeedConnectionAsync(string email = "test@gmail.com")
    {
        using var db = _factory.CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User"
        };
        db.Users.Add(user);

        var connection = new EmailConnection
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Email = email,
            SubjectId = $"sub-{Guid.NewGuid()}",
            RefreshToken = "test-refresh-token",
            GrantedScopes = "gmail.readonly",
            Status = EmailConnectionStatus.Active
        };
        db.EmailConnections.Add(connection);

        await db.SaveChangesAsync();
        return connection.Id;
    }
}
