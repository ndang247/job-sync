using System.ComponentModel.DataAnnotations;

namespace api_contracts.Requests;

/// <summary>Payload containing a refresh token.</summary>
public sealed record RefreshTokenRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}
