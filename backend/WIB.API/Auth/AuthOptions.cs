namespace WIB.API.Auth;

public class AuthOptions
{
    public string Issuer { get; set; } = "wib";
    public string Audience { get; set; } = "wib-clients";
    public string Key { get; set; } = "dev-secret-change-me";
    public List<AuthUser> Users { get; set; } = new();
}

public class AuthUser
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty; // DEV ONLY
    public string Role { get; set; } = "wmc"; // wmc|devices
}

