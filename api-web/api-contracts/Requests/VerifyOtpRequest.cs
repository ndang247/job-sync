using System.ComponentModel.DataAnnotations;

namespace api_contracts.Requests;

/// <summary>Payload for verifying an email one-time code.</summary>
public sealed record VerifyOtpRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, RegularExpression(@"^\d{6}$")]
    public required string Code { get; init; }
}
