using core.Interfaces;
using core.Models;

namespace infrastructure.Services;

public class GeminiService : IGeminiService
{
    public Task<List<JobApplication>> ClassifyBatchAsync(List<EmailMessage> emails, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<List<JobApplication>> DeduplicateAsync(List<JobApplication> applications, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
