using System.IdentityModel.Tokens.Jwt;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using api_contracts.Responses;
using core.Entities;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using web_api.Options;

namespace web_api.Authentication;

public sealed class AuthTokenService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<JwtOptions> options) : IAuthTokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<TokenResponse> IssueAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        var familyId = Guid.NewGuid();
        return await CreateTokenPairAsync(user, familyId, cancellationToken);
    }

    public async Task<TokenResponse?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

        var tokenHash = HashRefreshToken(refreshToken);
        var storedToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (storedToken.RevokedAt is not null)
        {
            await RevokeFamilyByIdAsync(storedToken.FamilyId, now, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (storedToken.ExpiresAt <= now)
        {
            return null;
        }

        var rawReplacement = GenerateRefreshToken();
        var replacementHash = HashRefreshToken(rawReplacement);
        var replacement = CreateRefreshToken(
            storedToken.User,
            storedToken.FamilyId,
            replacementHash,
            now);

        storedToken.RevokedAt = now;
        storedToken.ReplacedByTokenHash = replacementHash;
        dbContext.RefreshTokens.Add(replacement);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return CreateResponse(storedToken.User, rawReplacement, replacement.ExpiresAt, now);
    }

    public async Task RevokeFamilyAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashRefreshToken(refreshToken);
        var storedToken = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null)
        {
            return;
        }

        await RevokeFamilyByIdAsync(
            storedToken.FamilyId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task<TokenResponse> CreateTokenPairAsync(
        User user,
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rawRefreshToken = GenerateRefreshToken();
        var refreshToken = CreateRefreshToken(
            user,
            familyId,
            HashRefreshToken(rawRefreshToken),
            now);

        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateResponse(user, rawRefreshToken, refreshToken.ExpiresAt, now);
    }

    private TokenResponse CreateResponse(
        User user,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAt,
        DateTimeOffset now)
    {
        var accessTokenExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            accessTokenExpiresAt.UtcDateTime,
            credentials);

        return new TokenResponse(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(token),
            refreshToken,
            checked((int)TimeSpan.FromMinutes(_options.AccessTokenMinutes).TotalSeconds),
            refreshTokenExpiresAt);
    }

    private RefreshToken CreateRefreshToken(
        User user,
        Guid familyId,
        string tokenHash,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        FamilyId = familyId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now.AddDays(_options.RefreshTokenDays)
    };

    private async Task RevokeFamilyByIdAsync(
        Guid familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        var activeTokens = await dbContext.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = revokedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
