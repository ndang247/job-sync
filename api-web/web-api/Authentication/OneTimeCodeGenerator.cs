using System.Security.Cryptography;
using web_api.Interfaces;

namespace web_api.Authentication;

public sealed class OneTimeCodeGenerator : IOneTimeCodeGenerator
{
    public string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
