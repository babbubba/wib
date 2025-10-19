using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WIB.Application.Interfaces;

namespace WIB.API.Controllers;

[ApiController]
[Route("auth")]
[Route("api/[controller]")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return ToActionResult(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Get client info from request headers/context
        request.DeviceInfo = Request.Headers["User-Agent"].FirstOrDefault();
        request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _authService.LoginAsync(request);
        return ToActionResult(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request);
        return ToActionResult(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        var result = await _authService.LogoutAsync(request.RefreshToken);
        return ToActionResult(result);
    }

    [HttpPost("revoke-all")]
    [Authorize]
    public async Task<IActionResult> RevokeAllTokens()
    {
        var userId = GetCurrentUserId();
        var result = await _authService.RevokeAllTokensAsync(userId);
        return ToActionResult(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = GetCurrentUserId();
        var result = await _authService.GetCurrentUserAsync(userId);
        return ToActionResult(result);
    }

}

public class LogoutRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

