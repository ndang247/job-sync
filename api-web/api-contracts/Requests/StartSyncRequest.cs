namespace api_contracts.Requests;

public sealed record StartSyncRequest(
    Guid EmailConnectionId);
