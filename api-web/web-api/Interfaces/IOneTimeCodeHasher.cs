namespace web_api.Interfaces;

public interface IOneTimeCodeHasher
{
    string Hash(string destination, string purpose, string code);
    bool Verify(string expectedHash, string destination, string purpose, string code);
}
