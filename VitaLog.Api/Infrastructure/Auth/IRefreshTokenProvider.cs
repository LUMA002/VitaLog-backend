namespace VitaLog.Api.Infrastructure.Auth;

public interface IRefreshTokenProvider
{
    (string PlainTextToken, string TokenHash, DateTimeOffset ExpiresAtUtc) CreateRefreshToken();
    string Hash(string plainTextToken);
}