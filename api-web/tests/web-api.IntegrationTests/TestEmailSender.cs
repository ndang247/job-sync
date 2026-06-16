using System.Collections.Concurrent;
using core.Interfaces;

namespace web_api.IntegrationTests;

public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _codes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _failures =
        new(StringComparer.OrdinalIgnoreCase);

    public Task SendOtpAsync(
        string email,
        string code,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        if (_failures.TryRemove(email, out _))
            throw new InvalidOperationException("Test delivery failure.");

        _codes[email] = code;
        return Task.CompletedTask;
    }

    public bool TryGetCode(string email, out string code) =>
        _codes.TryGetValue(email, out code!);

    public void FailNextDeliveryTo(string email) => _failures[email] = 0;
}
