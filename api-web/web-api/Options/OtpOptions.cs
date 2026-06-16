using System.ComponentModel.DataAnnotations;

namespace web_api.Options;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    [Required, MinLength(32)]
    public string Pepper { get; init; } = string.Empty;

    [Range(60, 900)]
    public int ExpirationSeconds { get; init; } = 300;

    [Range(1, 10)]
    public int MaxAttempts { get; init; } = 5;

    [Range(1, 300)]
    public int ResendCooldownSeconds { get; init; } = 60;
}
