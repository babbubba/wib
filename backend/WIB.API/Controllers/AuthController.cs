using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WIB.API.Auth;

namespace WIB.API.Controllers;

public record TokenRequest(string Username, string Password);
public record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Role);

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IOptions<AuthOptions> _opts;
    public AuthController(IOptions<AuthOptions> opts) { _opts = opts; }

    [HttpPost("token")]
    public ActionResult<TokenResponse> Token([FromBody] TokenRequest req)
    {
        var user = _opts.Value.Users.FirstOrDefault(u => u.Username == req.Username && u.Password == req.Password);
        if (user == null) return Unauthorized();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Value.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };
        var token = new JwtSecurityToken(
            issuer: _opts.Value.Issuer,
            audience: _opts.Value.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.WriteToken(token);
        return Ok(new TokenResponse(jwt, "Bearer", 8 * 3600, user.Role));
    }
}

