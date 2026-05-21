using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using core.Entities;
using core.Enums;

namespace web_api.IntegrationTests.Controllers;

public class ConnectionsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public ConnectionsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_NoConnections_ReturnsOkWithEmptyArray()
    {
        var response = await _client.GetAsync("/api/v1/connections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
    }

    [Fact]
    public async Task GetAll_WithConnections_ReturnsActiveConnections()
    {
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = userId,
                FirstName = "Test",
                LastName = "User"
            });

            db.EmailConnections.Add(new EmailConnection
            {
                Id = connectionId,
                UserId = userId,
                Email = "test@gmail.com",
                SubjectId = "sub-123",
                RefreshToken = "refresh-token",
                GrantedScopes = "openid email",
                Provider = EmailConnectionProvider.Gmail,
                Status = EmailConnectionStatus.Active
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/connections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, content.ValueKind);

        var connections = content.EnumerateArray().ToList();
        var match = connections.FirstOrDefault(c => c.GetProperty("id").GetString() == connectionId.ToString());
        Assert.NotEqual(default, match);
        Assert.Equal("test@gmail.com", match.GetProperty("email").GetString());
    }

    [Fact]
    public async Task GetAll_DoesNotReturnSoftDeletedConnections()
    {
        var userId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = userId,
                FirstName = "Soft",
                LastName = "Delete"
            });

            db.EmailConnections.AddRange(
                new EmailConnection
                {
                    Id = activeId,
                    UserId = userId,
                    Email = "active@gmail.com",
                    SubjectId = "sub-active",
                    RefreshToken = "token-active",
                    GrantedScopes = "openid",
                    Provider = EmailConnectionProvider.Gmail,
                    Status = EmailConnectionStatus.Active
                },
                new EmailConnection
                {
                    Id = deletedId,
                    UserId = userId,
                    Email = "deleted@gmail.com",
                    SubjectId = "sub-deleted",
                    RefreshToken = "token-deleted",
                    GrantedScopes = "openid",
                    Provider = EmailConnectionProvider.Gmail,
                    Status = EmailConnectionStatus.Active,
                    DeletedAt = DateTime.UtcNow
                });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/connections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = content.EnumerateArray()
            .Select(c => c.GetProperty("id").GetString())
            .ToList();

        Assert.Contains(activeId.ToString(), ids);
        Assert.DoesNotContain(deletedId.ToString(), ids);
    }

    [Fact]
    public async Task GetAll_DoesNotExposeSensitiveFields()
    {
        var userId = Guid.NewGuid();

        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = userId,
                FirstName = "Sensitive",
                LastName = "Check"
            });

            db.EmailConnections.Add(new EmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Email = "sensitive@gmail.com",
                SubjectId = "sub-sensitive",
                RefreshToken = "secret-refresh-token",
                GrantedScopes = "openid email profile",
                Provider = EmailConnectionProvider.Gmail,
                Status = EmailConnectionStatus.Active
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/v1/connections");
        var content = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("secret-refresh-token", content);
        Assert.DoesNotContain("sub-sensitive", content);
        Assert.DoesNotContain("grantedScopes", content, StringComparison.OrdinalIgnoreCase);
    }
}
