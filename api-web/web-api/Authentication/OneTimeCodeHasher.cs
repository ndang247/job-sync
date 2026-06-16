using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using web_api.Interfaces;
using web_api.Options;

namespace web_api.Authentication;

public sealed class OneTimeCodeHasher : IOneTimeCodeHasher
{
    private readonly byte[] _pepper;

    public OneTimeCodeHasher(IOptions<OtpOptions> options)
    {
        _pepper = Encoding.UTF8.GetBytes(options.Value.Pepper);
    }

    public string Hash(string destination, string purpose, string code)
    {
        var payload = Encoding.UTF8.GetBytes($"{destination}\n{purpose}\n{code}");
        return Convert.ToHexString(HMACSHA256.HashData(_pepper, payload));
    }

    public bool Verify(string expectedHash, string destination, string purpose, string code)
    {
        var actualHash = Hash(destination, purpose, code);

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expectedHash),
            Convert.FromHexString(actualHash));
    }
}
