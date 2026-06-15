using core.Interfaces;
using infrastructure.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace infrastructure.Services;

public sealed class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendOtpAsync(
        string email,
        string code,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.SenderName, _options.SenderEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = "Your Job Sync verification code";
        message.Body = new TextPart("plain")
        {
            Text = $"Your verification code is {code}. It expires in {(int)expiresIn.TotalMinutes} minutes."
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            SecureSocketOptions.StartTls,
            cancellationToken);
        await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
