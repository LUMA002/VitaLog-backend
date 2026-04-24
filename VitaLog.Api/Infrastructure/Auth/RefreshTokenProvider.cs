using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace VitaLog.Api.Infrastructure.Auth;

public sealed class RefreshTokenProvider(IOptions<JwtOptions> options, TimeProvider timeProvider) : IRefreshTokenProvider
{
    private readonly JwtOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    public (string PlainTextToken, string TokenHash, DateTimeOffset ExpiresAtUtc) CreateRefreshToken()
    {
        var plainTextToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var tokenHash = Hash(plainTextToken);
        var expiresAtUtc = _timeProvider.GetUtcNow().AddDays(_options.RefreshTokenDays);

        return (plainTextToken, tokenHash, expiresAtUtc);
    }

    public string Hash(string plainTextToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plainTextToken));
        return Convert.ToHexString(hash);
    }
}