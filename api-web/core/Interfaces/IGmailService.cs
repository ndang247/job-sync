namespace core.Interfaces;

public class EmailMessage
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

public interface IGmailService
{
    Task<List<EmailMessage>> FetchEmailsAsync(Guid userId, CancellationToken cancellationToken = default);
}
