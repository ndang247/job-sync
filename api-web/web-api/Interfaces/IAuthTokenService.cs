using api_contracts.Responses;
using core.Entities;

namespace web_api.Interfaces;

public interface IAuthTokenService
{
    Task<TokenResponse> IssueAsync(User user, CancellationToken cancellationToken = default);
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task RevokeFamilyAsync(string refreshToken, CancellationToken cancellationToken = default);
}
