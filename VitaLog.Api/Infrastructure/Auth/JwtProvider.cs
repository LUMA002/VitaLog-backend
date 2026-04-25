using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VitaLog.Api.Domain.Enums;

namespace VitaLog.Api.Infrastructure.Auth;

public sealed class JwtProvider : IJwtProvider
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    // Instantiated once per provider lifetime to reduce memory allocation
    private readonly SigningCredentials _credentials;

    // Cached to avoid reflection overhead on every token generation
    private static readonly Role[] _allRoles = 
        [.. Enum.GetValues<Role>().Where(static r => r != Role.None && IsSingleBitFlag(r))];

    public JwtProvider(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(Guid userId, string email, Role roles)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        // Pre-allocating capacity avoids internal array resizing
        var claims = new List<Claim>(3 + _allRoles.Length)
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        foreach (var role in _allRoles)
        {
            //if (roles.HasFlag(role))
            if ((roles & role) == role)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return (token, expires);
    }

    private static bool IsSingleBitFlag(Role role)
    {
        var value = (int)role;
        return (value & (value - 1)) == 0;
    }
}