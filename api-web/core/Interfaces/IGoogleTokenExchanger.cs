namespace core.Interfaces;

public record OAuthTokenResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    string SubjectId,
    string Email,
    string GrantedScopes);

public interface IGoogleTokenExchanger
{
    Task<OAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);
}
