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
