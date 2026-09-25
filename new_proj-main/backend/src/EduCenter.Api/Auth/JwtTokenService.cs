using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EduCenter.Modules.Identity.Domain;
using Microsoft.IdentityModel.Tokens;

namespace EduCenter.Api.Auth;

public record AccessToken(string Value, DateTimeOffset ExpiresAt);
public record RefreshTokenPair(string RawValue, string Hash, DateTimeOffset ExpiresAt);

public class JwtTokenService(IConfiguration configuration)
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public AccessToken CreateAccessToken(User user, IEnumerable<string> roleCodes)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("org", user.OrganizationId.ToString()),
            new(ClaimTypes.Email, user.Email)
        };
        claims.AddRange(roleCodes.Select(code => new Claim(ClaimTypes.Role, code)));

        var expiresAt = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>Generates a random opaque refresh token. Only its hash is meant to be persisted.</summary>
    public RefreshTokenPair CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new RefreshTokenPair(raw, Hash(raw), DateTimeOffset.UtcNow.Add(RefreshTokenLifetime));
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
