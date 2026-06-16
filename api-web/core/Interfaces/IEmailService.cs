namespace core.Interfaces;

public class EmailMessage
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
}

public interface IEmailService
{
    Task<List<EmailMessage>> FetchEmailsAsync(
        Guid emailConnectionId,
        DateTime syncStartUtc,
        DateTime syncEndUtcExclusive,
        CancellationToken cancellationToken = default);
}
