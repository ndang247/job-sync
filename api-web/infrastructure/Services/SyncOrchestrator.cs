using core.Interfaces;
using core.Models;

namespace infrastructure.Services;

public class SyncOrchestrator : ISyncOrchestrator
{
    private readonly IEmailService _emailService;
    private readonly IAIService _aiService;
    private const int BatchSize = 20;

    public SyncOrchestrator(IEmailService emailService, IAIService aiService)
    {
        _emailService = emailService;
        _aiService = aiService;
    }

    public async Task<List<JobApplication>> ExecuteSyncAsync(Guid jobId, Guid emailConnectionId, ISyncProgressReporter progressReporter, CancellationToken cancellationToken = default)
    {
        await progressReporter.ReportProgressAsync(jobId, "Fetching emails", 5, cancellationToken);

        var emails = await _emailService.FetchEmailsAsync(emailConnectionId, cancellationToken);

        if (emails.Count == 0)
            return new List<JobApplication>();

        var allApplications = new List<JobApplication>();
        var batches = emails.Chunk(BatchSize).ToList();
        var totalBatches = batches.Count;

        for (var i = 0; i < totalBatches; i++)
        {
            var percent = 10 + (int)((80.0 / totalBatches) * i);
            await progressReporter.ReportProgressAsync(jobId, $"Processing batch {i + 1}/{totalBatches}", percent, cancellationToken);

            var batchResults = await _aiService.ClassifyBatchAsync(batches[i].ToList(), cancellationToken);
            allApplications.AddRange(batchResults);
        }

        if (allApplications.Count == 0)
            return new List<JobApplication>();

        await progressReporter.ReportProgressAsync(jobId, "Deduplicating results", 90, cancellationToken);

        var deduplicated = await _aiService.DeduplicateAsync(allApplications, cancellationToken);

        await progressReporter.ReportProgressAsync(jobId, "Done", 100, cancellationToken);

        return deduplicated;
    }
}
