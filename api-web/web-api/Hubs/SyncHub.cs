using infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using web_api.Authentication;

namespace web_api.Hubs;

[Authorize]
public sealed class SyncHub(AppDbContext dbContext) : Hub
{
    public async Task JoinJob(Guid jobId)
    {
        var cancellationToken = Context.ConnectionAborted;
        if (Context.User is null ||
            !Context.User.TryGetUserId(out var userId) ||
            !await dbContext.SyncJobs.AnyAsync(
                job => job.Id == jobId && job.UserId == userId,
                cancellationToken))
        {
            throw new HubException("Sync job not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"sync-{jobId}");
    }

    public async Task LeaveJob(Guid jobId)
    {
        var cancellationToken = Context.ConnectionAborted;
        if (Context.User is null ||
            !Context.User.TryGetUserId(out var userId) ||
            !await dbContext.SyncJobs.AnyAsync(
                job => job.Id == jobId && job.UserId == userId,
                cancellationToken))
        {
            throw new HubException("Sync job not found.");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sync-{jobId}");
    }
}
