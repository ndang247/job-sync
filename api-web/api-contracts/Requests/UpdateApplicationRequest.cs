namespace api_contracts.Requests;

public sealed record UpdateApplicationRequest(
    string CompanyName,
    string JobRole,
    string Status,
    string AppliedDate);
