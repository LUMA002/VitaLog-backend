namespace VitaLog.Api.Domain.Entities;

public sealed class UserRefreshToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }

    /// <summary>
    /// Hashed token
    /// </summary>
    public string Token { get; init; } = string.Empty; // Hashed
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}