namespace web_api.Interfaces;

public interface IGoogleOAuthStateStore
{
    string Issue(Guid userId);
    bool TryConsume(string state, out Guid userId);
}
