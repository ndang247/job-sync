namespace api_contracts.Responses;

public sealed record ApplicationListItemResponse(
    Guid Id,
    string CompanyName,
    string JobRole,
    string AppliedDate,
    string Status,
    string Email,
    DateTime CreatedAt);
