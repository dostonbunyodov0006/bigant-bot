using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Bigant.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Bigant.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    /// <summary>Kamida 32 belgi. user-secrets yoki environment variable da saqlang.</summary>
    public string Key { get; set; } = default!;
    public string Issuer { get; set; } = "bigant";
    public string Audience { get; set; } = "bigant";
    public int ExpiresHours { get; set; } = 12;
}

public sealed class JwtService
{
    private readonly JwtOptions _opt;
    public JwtService(JwtOptions opt) => _opt = opt;

    public (string token, DateTime expiresAt) Create(AppUser user)
    {
        var expires = DateTime.UtcNow.AddHours(_opt.ExpiresHours);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("fullName", user.FullName)
        };

        var jwt = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims,
            expires: expires, signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
