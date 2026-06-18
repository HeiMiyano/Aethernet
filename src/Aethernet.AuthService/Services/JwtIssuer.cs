using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Aethernet.AuthService.Services;

public sealed class JwtIssuer : IJwtIssuer
{
    private readonly IConfiguration _cfg;
    public JwtIssuer(IConfiguration cfg) { _cfg = cfg; }

    public (string Token, DateTime ExpiresAt) IssueAccessToken(string uid, bool isAdmin, bool isModerator)
    {
        var lifetime = TimeSpan.FromHours(int.Parse(_cfg["Jwt:AccessLifetimeHours"] ?? "12"));
        var expires  = DateTime.UtcNow.Add(lifetime);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uid),
            new(JwtRegisteredClaimNames.Sub, uid),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (isAdmin)     claims.Add(new Claim(ClaimTypes.Role, "admin"));
        if (isModerator) claims.Add(new Claim(ClaimTypes.Role, "moderator"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:SigningKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer:   _cfg["Jwt:Issuer"]   ?? "aethernet-auth",
            audience: _cfg["Jwt:Audience"] ?? "aethernet-hub",
            claims:   claims,
            expires:  expires,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
