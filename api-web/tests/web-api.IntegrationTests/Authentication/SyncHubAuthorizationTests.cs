using core.Entities;
using core.Enums;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace web_api.IntegrationTests.Authentication;

public sealed class SyncHubAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SyncHubAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task JoinJob_AnotherUsersJob_ThrowsHubException()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var jobId = await SeedJobAsync(ownerId);
        await using var connection = CreateConnection(callerId);
        await connection.StartAsync();

        var exception = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync("JoinJob", jobId));

        Assert.Contains("Sync job not found.", exception.Message);
    }

    [Fact]
    public async Task Connect_WithoutAccessToken_IsRejected()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/sync", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task JoinJob_OwnJob_Succeeds()
    {
        var userId = Guid.NewGuid();
        var jobId = await SeedJobAsync(userId);
        await using var connection = CreateConnection(userId);
        await connection.StartAsync();

        await connection.InvokeAsync("JoinJob", jobId);
    }

    private HubConnection CreateConnection(Guid userId)
    {
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/sync", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () =>
                    Task.FromResult<string?>(
                        CustomWebApplicationFactory.CreateAccessToken(userId));
            })
            .Build();
    }

    private async Task<Guid> SeedJobAsync(Guid userId)
    {
        var connectionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        using var db = _factory.CreateDbContext();
        db.Users.Add(new User
        {
            Id = userId,
            UserName = $"hub-{userId:N}@example.com",
            Email = $"hub-{userId:N}@example.com",
            FirstName = "Hub",
            LastName = "User"
        });
        db.EmailConnections.Add(new EmailConnection
        {
            Id = connectionId,
            UserId = userId,
            Email = "hub@gmail.com",
            SubjectId = $"subject-{Guid.NewGuid():N}",
            RefreshToken = "refresh-token",
            GrantedScopes = "gmail.readonly"
        });
        db.SyncJobs.Add(new SyncJob
        {
            Id = jobId,
            UserId = userId,
            EmailConnectionId = connectionId,
            Status = SyncJobStatus.Pending
        });
        await db.SaveChangesAsync();
        return jobId;
    }
}
