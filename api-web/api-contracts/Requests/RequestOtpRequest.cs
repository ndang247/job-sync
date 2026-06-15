using System.ComponentModel.DataAnnotations;

namespace api_contracts.Requests;

/// <summary>Payload for requesting an email one-time code.</summary>
public sealed record RequestOtpRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }
}
