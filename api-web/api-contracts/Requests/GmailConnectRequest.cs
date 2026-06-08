namespace api_contracts.Requests;

public sealed record GmailConnectRequest(
    string Code,
    Guid? UserId);
