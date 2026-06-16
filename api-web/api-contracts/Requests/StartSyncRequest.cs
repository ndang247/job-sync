namespace api_contracts.Requests;

public sealed record StartSyncRequest(
    Guid EmailConnectionId,
    SyncDateRangeRequest? DateRange = null);

public sealed record SyncDateRangeRequest(
    string? StartDate,
    string? EndDate = null,
    string? TimeZone = null);
