using System.ComponentModel.DataAnnotations;

namespace VitaLog.Api.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(3)]
    public string Issuer { get; init; } = string.Empty;

    [Required, MinLength(3)]
    public string Audience { get; init; } = string.Empty;

    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; init; } = 30;
}