namespace core.Interfaces;

public interface IEmailSender
{
    Task SendOtpAsync(
        string email,
        string code,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);
}
