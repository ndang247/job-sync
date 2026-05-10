using core.Interfaces;

namespace infrastructure.Services;

public class GmailService : IGmailService
{
    public Task<List<EmailMessage>> FetchEmailsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
