namespace api_contracts.Responses;

/// <summary>Access and refresh tokens issued for an authenticated user.</summary>
public sealed record TokenResponse(
    string TokenType,
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    DateTimeOffset RefreshTokenExpiresAt);
