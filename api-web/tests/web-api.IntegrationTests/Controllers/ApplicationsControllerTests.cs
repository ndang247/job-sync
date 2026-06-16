using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using core.Entities;
using core.Enums;
using core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace web_api.IntegrationTests.Controllers;

public class ApplicationsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _userId = Guid.NewGuid();

    public ApplicationsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        ResetDatabase();
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = _userId,
                UserName = $"applications-{_userId:N}@example.com",
                Email = $"applications-{_userId:N}@example.com",
                FirstName = "Test",
                LastName = "User"
            });
            db.SaveChanges();
        }
        _client = factory.CreateAuthenticatedClient(_userId);
    }

    private void ResetDatabase()
    {
        using var db = _factory.CreateDbContext();
        db.JobApplications.RemoveRange(db.JobApplications.IgnoreQueryFilters());
        db.SyncJobs.RemoveRange(db.SyncJobs.IgnoreQueryFilters());
        db.EmailConnections.RemoveRange(db.EmailConnections);
        db.Users.RemoveRange(db.Users);
        db.SaveChanges();

        _factory.Services.GetRequiredService<IApplicationListCacheState>().Invalidate();
    }

    [Fact]
    public async Task GetAll_NoApplications_ReturnsOkWithArray()
    {
        var response = await _client.GetAsync("/api/v1/applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, content.ValueKind);
        Assert.Equal(JsonValueKind.Array, content.GetProperty("items").ValueKind);
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
                    UserId = _userId,
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
                    UserId = _userId,
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

        var items = await ReadItemsAsync(response);
        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
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
                UserId = _userId,
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
        var items = await ReadItemsAsync(response);
        var item = items[0];

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
                UserId = _userId,
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
                UserId = _userId,
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
        var items = content.GetProperty("items");

        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
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
                UserId = _userId,
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
                UserId = _userId,
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
        var items = await ReadItemsAsync(response);

        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
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
                UserId = _userId,
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
        var items = await ReadItemsAsync(response);

        Assert.Equal("jobs@example.com", items[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task GetAll_DefaultPagination_ReturnsMetadataAndTenItems()
    {
        var connId = await SeedConnectionAsync();
        var testKey = Guid.NewGuid().ToString("N");

        await SeedApplicationsAsync(connId, testKey, 12, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var response = await _client.GetAsync("/api/v1/applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = content.GetProperty("items");
        Assert.Equal(10, items.GetArrayLength());
        Assert.Equal(1, content.GetProperty("page").GetInt32());
        Assert.Equal(10, content.GetProperty("pageSize").GetInt32());
        Assert.True(content.GetProperty("totalCount").GetInt32() >= 12);
        Assert.True(content.GetProperty("totalPages").GetInt32() >= 2);
        Assert.False(content.GetProperty("hasPrevious").GetBoolean());
        Assert.True(content.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task GetAll_PageTwo_ReturnsNextOrderedSlice()
    {
        var connId = await SeedConnectionAsync();
        var testKey = Guid.NewGuid().ToString("N");

        await SeedApplicationsAsync(connId, testKey, 12, new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var response = await _client.GetAsync("/api/v1/applications?page=2&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = content.GetProperty("items");
        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
            .ToList();

        Assert.Equal(2, content.GetProperty("page").GetInt32());
        Assert.Equal(5, content.GetProperty("pageSize").GetInt32());
        Assert.True(content.GetProperty("hasPrevious").GetBoolean());
        Assert.Contains($"{testKey}-Company-05", companies);
        Assert.Contains($"{testKey}-Company-09", companies);
        Assert.DoesNotContain($"{testKey}-Company-00", companies);
    }

    [Fact]
    public async Task GetAll_PageSizeIsCappedAtOneHundred()
    {
        var response = await _client.GetAsync("/api/v1/applications?pageSize=500");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, content.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetAll_CacheIsInvalidatedAfterServiceAddsApplications()
    {
        var connId = await SeedConnectionAsync();
        var testKey = Guid.NewGuid().ToString("N");
        var messageId = $"msg-cache-{testKey}";

        await SeedApplicationsAsync(connId, $"{testKey}-existing", 1, new DateTime(2034, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await _client.GetAsync("/api/v1/applications?page=1&pageSize=10");

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IJobApplicationService>();
            await service.AddApplicationsAsync(connId, new List<JobApplication>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = _userId,
                    CompanyName = $"{testKey}-InsertedCo",
                    JobRole = "Dev",
                    AppliedDate = "20-05-2026",
                    Status = JobApplicationStatus.Applied,
                    MessageId = messageId
                }
            });
        }

        using (var db = _factory.CreateDbContext())
        {
            var inserted = db.JobApplications.Single(ja => ja.MessageId == messageId);
            inserted.CreatedAt = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/applications?page=1&pageSize=10");
        var items = await ReadItemsAsync(response);
        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
            .ToList();

        Assert.Contains($"{testKey}-InsertedCo", companies);

        using var ownershipDb = _factory.CreateDbContext();
        Assert.Equal(
            _userId,
            ownershipDb.JobApplications.Single(ja => ja.MessageId == messageId).UserId);
    }

    [Fact]
    public async Task GetById_WithApplication_ReturnsSelectedApplication()
    {
        var connId = await SeedConnectionAsync("detail@example.com");
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "DetailCo",
                JobRole = "Platform Engineer",
                AppliedDate = "21-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/v1/applications/{appId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var item = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(appId, item.GetProperty("id").GetGuid());
        Assert.Equal("DetailCo", item.GetProperty("companyName").GetString());
        Assert.Equal("Platform Engineer", item.GetProperty("jobRole").GetString());
        Assert.Equal("21-05-2026", item.GetProperty("appliedDate").GetString());
        Assert.Equal("Applied", item.GetProperty("status").GetString());
        Assert.Equal("detail@example.com", item.GetProperty("email").GetString());
    }

    [Fact]
    public async Task GetById_MissingApplication_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/v1/applications/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ApplicationOwnedByAnotherUser_ReturnsNotFound()
    {
        var otherUserId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = $"other-{otherUserId:N}@example.com",
                Email = $"other-{otherUserId:N}@example.com",
                FirstName = "Other",
                LastName = "User"
            });
            db.EmailConnections.Add(new EmailConnection
            {
                Id = connectionId,
                UserId = otherUserId,
                Email = "other@gmail.com",
                SubjectId = $"subject-{Guid.NewGuid():N}",
                RefreshToken = "refresh-token",
                GrantedScopes = "gmail.readonly"
            });
            db.JobApplications.Add(new JobApplication
            {
                Id = applicationId,
                UserId = otherUserId,
                CompanyName = "HiddenCo",
                JobRole = "Engineer",
                AppliedDate = "15-06-2026",
                MessageId = $"message-{Guid.NewGuid():N}",
                EmailConnectionId = connectionId
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/v1/applications/{applicationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_SoftDeletedApplication_ReturnsNotFound()
    {
        var connId = await SeedConnectionAsync();
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "DeletedDetailCo",
                JobRole = "Dev",
                AppliedDate = "21-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId,
                DeletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/v1/applications/{appId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesEditableFieldsAndKeepsEmailReadOnly()
    {
        var connId = await SeedConnectionAsync("readonly@example.com");
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "BeforeCo",
                JobRole = "Before Role",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PutAsJsonAsync($"/api/v1/applications/{appId}", new
        {
            companyName = "AfterCo",
            jobRole = "After Role",
            status = "Interviewing",
            appliedDate = "22-05-2026"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var item = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AfterCo", item.GetProperty("companyName").GetString());
        Assert.Equal("After Role", item.GetProperty("jobRole").GetString());
        Assert.Equal("Interviewing", item.GetProperty("status").GetString());
        Assert.Equal("22-05-2026", item.GetProperty("appliedDate").GetString());
        Assert.Equal("readonly@example.com", item.GetProperty("email").GetString());

        using var verifyDb = _factory.CreateDbContext();
        var stored = await verifyDb.JobApplications.SingleAsync(ja => ja.Id == appId);
        Assert.Equal("AfterCo", stored.CompanyName);
        Assert.Equal("After Role", stored.JobRole);
        Assert.Equal("22-05-2026", stored.AppliedDate);
        Assert.Equal("Interviewing", stored.Status.ToString());
        Assert.Equal(connId, stored.EmailConnectionId);
        Assert.NotNull(stored.UpdatedAt);
    }

    [Fact]
    public async Task Put_MissingApplication_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"/api/v1/applications/{Guid.NewGuid()}", new
        {
            companyName = "AfterCo",
            jobRole = "After Role",
            status = "Applied",
            appliedDate = "22-05-2026"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Engineer", "Applied", "22-05-2026")]
    [InlineData("Company", "", "Applied", "22-05-2026")]
    [InlineData("Company", "Engineer", "Unknown", "22-05-2026")]
    [InlineData("Company", "Engineer", "1", "22-05-2026")]
    [InlineData("Company", "Engineer", "Applied", "2026-05-22")]
    public async Task Put_InvalidPayload_ReturnsBadRequest(
        string companyName,
        string jobRole,
        string status,
        string appliedDate)
    {
        var connId = await SeedConnectionAsync();
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "BeforeCo",
                JobRole = "Before Role",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PutAsJsonAsync($"/api/v1/applications/{appId}", new
        {
            companyName,
            jobRole,
            status,
            appliedDate
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidatesCachedApplicationList()
    {
        var connId = await SeedConnectionAsync();
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "CachedBeforeCo",
                JobRole = "Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        await _client.GetAsync("/api/v1/applications?page=1&pageSize=10");

        var update = await _client.PutAsJsonAsync($"/api/v1/applications/{appId}", new
        {
            companyName = "CachedAfterCo",
            jobRole = "Dev",
            status = "Applied",
            appliedDate = "20-05-2026"
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var response = await _client.GetAsync("/api/v1/applications?page=1&pageSize=10");
        var items = await ReadItemsAsync(response);
        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
            .ToList();

        Assert.Contains("CachedAfterCo", companies);
        Assert.DoesNotContain("CachedBeforeCo", companies);
    }

    [Fact]
    public async Task Delete_WithApplication_ReturnsNoContentAndSoftDeletes()
    {
        var connId = await SeedConnectionAsync();
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "DeleteCo",
                JobRole = "Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        var delete = await _client.DeleteAsync($"/api/v1/applications/{appId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using (var verifyDb = _factory.CreateDbContext())
        {
            var stored = await verifyDb.JobApplications
                .IgnoreQueryFilters()
                .SingleAsync(ja => ja.Id == appId);
            Assert.NotNull(stored.DeletedAt);
        }

        var response = await _client.GetAsync("/api/v1/applications");
        var items = await ReadItemsAsync(response);
        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
            .ToList();

        Assert.DoesNotContain("DeleteCo", companies);
    }

    [Fact]
    public async Task Delete_MissingApplication_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/v1/applications/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDeletedApplication_ReturnsNotFound()
    {
        var connId = await SeedConnectionAsync();
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "AlreadyDeletedCo",
                JobRole = "Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId,
                DeletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.DeleteAsync($"/api/v1/applications/{appId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_InvalidatesCachedApplicationList()
    {
        var connId = await SeedConnectionAsync();
        var appId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.JobApplications.Add(new JobApplication
            {
                Id = appId,
                UserId = _userId,
                CompanyName = "CachedDeleteCo",
                JobRole = "Dev",
                AppliedDate = "20-05-2026",
                Status = JobApplicationStatus.Applied,
                MessageId = $"msg-{Guid.NewGuid()}",
                EmailConnectionId = connId
            });
            await db.SaveChangesAsync();
        }

        await _client.GetAsync("/api/v1/applications?page=1&pageSize=10");

        var delete = await _client.DeleteAsync($"/api/v1/applications/{appId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var response = await _client.GetAsync("/api/v1/applications?page=1&pageSize=10");
        var items = await ReadItemsAsync(response);
        var companies = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("companyName").GetString())
            .ToList();

        Assert.DoesNotContain("CachedDeleteCo", companies);
    }

    private static async Task<JsonElement> ReadItemsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        return content.GetProperty("items");
    }

    private async Task SeedApplicationsAsync(Guid connId, string testKey, int count, DateTime newestCreatedAt)
    {
        var ids = new List<Guid>();
        using (var db = _factory.CreateDbContext())
        {
            for (var i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                db.JobApplications.Add(new JobApplication
                {
                    Id = id,
                    UserId = _userId,
                    CompanyName = $"{testKey}-Company-{i:00}",
                    JobRole = "Dev",
                    AppliedDate = "20-05-2026",
                    Status = JobApplicationStatus.Applied,
                    MessageId = $"msg-{testKey}-{i:00}",
                    EmailConnectionId = connId
                });
            }

            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var application = await db.JobApplications.FindAsync(ids[i]);
                application!.CreatedAt = newestCreatedAt.AddMinutes(-i);
            }

            await db.SaveChangesAsync();
        }
    }

    private async Task<Guid> SeedConnectionAsync(string email = "test@gmail.com")
    {
        using var db = _factory.CreateDbContext();
        var connection = new EmailConnection
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
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
