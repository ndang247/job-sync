using Microsoft.Extensions.Hosting;

namespace worker;

public class SyncBackgroundService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }
}
