namespace web_api.Authentication;

public interface IGoogleOAuthStateStore
{
    string Issue(Guid userId);
    bool TryConsume(string state, out Guid userId);
}
