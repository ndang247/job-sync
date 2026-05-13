using core.Interfaces;
using core.Models;

namespace infrastructure.Services;

public class SyncOrchestrator : ISyncOrchestrator
{
    private readonly IGmailService _gmailService;
    private readonly IGeminiService _geminiService;
    private const int BatchSize = 20;

    public SyncOrchestrator(IGmailService gmailService, IGeminiService geminiService)
    {
        _gmailService = gmailService;
        _geminiService = geminiService;
    }

    public async Task<List<JobApplication>> ExecuteSyncAsync(Guid jobId, Guid userId, ISyncProgressReporter progressReporter, CancellationToken cancellationToken = default)
    {
        await progressReporter.ReportProgressAsync(jobId, "Fetching emails", 5, cancellationToken);

        var emails = await _gmailService.FetchEmailsAsync(userId, cancellationToken);

        if (emails.Count == 0)
            return new List<JobApplication>();

        var allApplications = new List<JobApplication>();
        var batches = emails.Chunk(BatchSize).ToList();
        var totalBatches = batches.Count;

        for (var i = 0; i < totalBatches; i++)
        {
            var percent = 10 + (int)((80.0 / totalBatches) * i);
            await progressReporter.ReportProgressAsync(jobId, $"Processing batch {i + 1}/{totalBatches}", percent, cancellationToken);

            var batchResults = await _geminiService.ClassifyBatchAsync(batches[i].ToList(), cancellationToken);
            allApplications.AddRange(batchResults);
        }

        if (allApplications.Count == 0)
            return new List<JobApplication>();

        await progressReporter.ReportProgressAsync(jobId, "Deduplicating results", 90, cancellationToken);

        var deduplicated = await _geminiService.DeduplicateAsync(allApplications, cancellationToken);

        await progressReporter.ReportProgressAsync(jobId, "Done", 100, cancellationToken);

        return deduplicated;
    }
}
