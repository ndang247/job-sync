namespace api_contracts.Requests;

public class StartSyncRequest
{
    public Guid UserId { get; set; }
    public Guid EmailConnectionId { get; set; }
}
