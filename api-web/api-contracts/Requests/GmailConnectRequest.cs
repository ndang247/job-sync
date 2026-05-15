namespace api_contracts.Requests;

public class GmailConnectRequest
{
    public string Code { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
}
