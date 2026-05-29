namespace api_contracts.Responses;

public sealed record ApplicationListResponse(
    IReadOnlyList<ApplicationListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);
