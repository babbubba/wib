using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WIB.Application.Common;
using WIB.Application.Interfaces;
using WIB.Domain;
using WIB.Infrastructure.Data;
using BCrypt.Net;

namespace WIB.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly WibDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;

    public AuthService(WibDbContext context, ITokenService tokenService, IConfiguration configuration)
    {
        _context = context;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    public async Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Check if user already exists
        var existingUser = await _context.Users
            .Where(u => u.Email.ToLower() == request.Email.ToLower())
            .FirstOrDefaultAsync(cancellationToken);

        if (existingUser != null)
        {
            return Result<AuthResult>.Failure("User with this email already exists");
        }

        // Create new user
        var user = new User
        {
            Email = request.Email.ToLower().Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            EmailVerified = false // In production, this would require email verification
        };

        _context.Users.Add(user);
        
        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);
        
        _context.RefreshTokens.Add(refreshToken);
        
        await _context.SaveChangesAsync(cancellationToken);

        var result = new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(double.Parse(_configuration["Jwt:AccessTokenExpirationMinutes"] ?? "15")),
            User = MapToUserProfile(user)
        };

        return Result<AuthResult>.Success(result);
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.Username.ToLower() == request.Username.ToLower() && u.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return Result<AuthResult>.Failure("Invalid username or password");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Result<AuthResult>.Failure("Invalid username or password");
        }

        // Update last login
        user.LastLoginAt = DateTimeOffset.UtcNow;

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id, request.DeviceInfo, request.IpAddress);
        
        _context.RefreshTokens.Add(refreshToken);
        
        await _context.SaveChangesAsync(cancellationToken);

        var result = new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(double.Parse(_configuration["Jwt:AccessTokenExpirationMinutes"] ?? "15")),
            User = MapToUserProfile(user)
        };

        return Result<AuthResult>.Success(result);
    }

    public async Task<Result<AuthResult>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _tokenService.ValidateRefreshTokenAsync(request.RefreshToken, cancellationToken))
        {
            return Result<AuthResult>.Failure("Invalid or expired refresh token");
        }

        var user = await _tokenService.GetUserFromRefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (user == null || !user.IsActive)
        {
            return Result<AuthResult>.Failure("Invalid refresh token or user not active");
        }

        // Revoke old refresh token
        await _tokenService.RevokeRefreshTokenAsync(request.RefreshToken, "Token refreshed", cancellationToken);

        // Generate new tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);
        
        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        var result = new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(double.Parse(_configuration["Jwt:AccessTokenExpirationMinutes"] ?? "15")),
            User = MapToUserProfile(user)
        };

        return Result<AuthResult>.Success(result);
    }

    public async Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        await _tokenService.RevokeRefreshTokenAsync(refreshToken, "User logout", cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RevokeAllTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _tokenService.RevokeAllUserRefreshTokensAsync(userId, "All tokens revoked by user", cancellationToken);
        return Result.Success();
    }

    public async Task<Result<User>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Where(u => u.Id == userId && u.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return Result<User>.Failure("User not found");
        }

        return Result<User>.Success(user);
    }

    private static UserProfile MapToUserProfile(User user)
    {
        return new UserProfile
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Roles = user.UserRoles?.Select(ur => ur.Role?.Name ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            EmailVerified = user.EmailVerified
        };
    }
}