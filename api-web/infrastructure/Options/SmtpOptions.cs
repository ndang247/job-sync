using System.ComponentModel.DataAnnotations;

namespace infrastructure.Options;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; init; } = "smtp.gmail.com";

    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    [Required, EmailAddress]
    public string UserName { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string SenderEmail { get; init; } = string.Empty;

    [Required]
    public string SenderName { get; init; } = "Job Sync";
}
