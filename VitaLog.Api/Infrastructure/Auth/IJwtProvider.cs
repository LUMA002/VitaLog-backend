using VitaLog.Api.Domain.Enums;

namespace VitaLog.Api.Infrastructure.Auth;

public interface IJwtProvider
{
    (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(Guid userId, string email, Role roles);
}