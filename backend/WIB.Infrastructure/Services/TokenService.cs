using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using WIB.Application.Interfaces;
using WIB.Domain;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly WibDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public TokenService(WibDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    public string GenerateAccessToken(User user)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = Encoding.ASCII.GetBytes(jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured"));
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName),
            new Claim("emailVerified", user.EmailVerified.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Temporary role assignment for access control: default UI role is "wmc".
        // When user roles are implemented in the domain, map them here accordingly.
        claims.Add(new Claim(ClaimTypes.Role, "wmc"));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(double.Parse(jwtSettings["AccessTokenExpirationMinutes"] ?? "15")),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
            Issuer = jwtSettings["Issuer"],
            Audience = jwtSettings["Audience"]
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    public RefreshToken GenerateRefreshToken(Guid userId, string? deviceInfo = null, string? ipAddress = null)
    {
        var refreshTokenBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(refreshTokenBytes);

        var jwtSettings = _configuration.GetSection("Jwt");
        var refreshTokenExpirationDays = double.Parse(jwtSettings["RefreshTokenExpirationDays"] ?? "30");

        return new RefreshToken
        {
            UserId = userId,
            Token = Convert.ToBase64String(refreshTokenBytes),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(refreshTokenExpirationDays),
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress
        };
    }

    public async Task<bool> ValidateRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var refreshToken = await _context.RefreshTokens
            .Where(rt => rt.Token == token && !rt.IsRevoked && rt.ExpiresAt > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(cancellationToken);

        return refreshToken != null;
    }

    public async Task<User?> GetUserFromRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .Where(rt => rt.Token == token && !rt.IsRevoked && rt.ExpiresAt > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(cancellationToken);

        return refreshToken?.User;
    }

    public async Task RevokeRefreshTokenAsync(string token, string reason = "User logout", CancellationToken cancellationToken = default)
    {
        var refreshToken = await _context.RefreshTokens
            .Where(rt => rt.Token == token)
            .FirstOrDefaultAsync(cancellationToken);

        if (refreshToken != null)
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedReason = reason;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokeAllUserRefreshTokensAsync(Guid userId, string reason = "Security revocation", CancellationToken cancellationToken = default)
    {
        var refreshTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in refreshTokens)
        {
            token.IsRevoked = true;
            token.RevokedReason = reason;
        }

        if (refreshTokens.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}