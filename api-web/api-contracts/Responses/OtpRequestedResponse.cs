namespace api_contracts.Responses;

/// <summary>Generic response returned after an OTP delivery attempt succeeds.</summary>
public sealed record OtpRequestedResponse(
    string Message,
    int ExpiresInSeconds,
    int ResendAfterSeconds);
