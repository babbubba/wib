using WIB.Domain;

namespace WIB.Application.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(Guid userId, string? deviceInfo = null, string? ipAddress = null);
    Task<bool> ValidateRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<User?> GetUserFromRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(string token, string reason = "User logout", CancellationToken cancellationToken = default);
    Task RevokeAllUserRefreshTokensAsync(Guid userId, string reason = "Security revocation", CancellationToken cancellationToken = default);
}