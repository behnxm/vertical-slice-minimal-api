using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace TaskTracker.IntegrationTests.Infrastructure;

public sealed class TestJwtTokenIssuer
{
    public const string Issuer = "tasktracker-test-issuer";
    public const string Audience = "tasktracker-api";

    private readonly RSA _rsa;
    public RsaSecurityKey SecurityKey { get; }

    public TestJwtTokenIssuer()
    {
        _rsa = RSA.Create(2048);
        SecurityKey = new RsaSecurityKey(_rsa)
        {
            KeyId = "test-jwt-key"
        };
    }

    public string CreateToken(
        string userId,
        string? displayName = null,
        params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("name", displayName ?? userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        if (scopes.Length > 0)
        {
            claims.Add(new Claim("scope", string.Join(' ', scopes)));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = DateTime.UtcNow.AddHours(2),
            SigningCredentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.RsaSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }
}
