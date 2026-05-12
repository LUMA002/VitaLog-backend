using System.IdentityModel.Tokens.Jwt;

namespace VitaLog.Api.Infrastructure.Auth;

public static class HttpContextAuthExtensions
{
    public static Guid GetCurrentUserId(this HttpContext context)
    {
        var userIdRaw = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(userIdRaw, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Missing or invalid user identifier in JWT.");
    }
}
