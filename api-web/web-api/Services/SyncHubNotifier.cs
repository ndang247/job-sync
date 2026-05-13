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
