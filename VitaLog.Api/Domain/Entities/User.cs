using VitaLog.Api.Domain.Enums;

namespace VitaLog.Api.Domain.Entities;

public sealed class User
{
    public Guid Id { get; init; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Role Roles { get; set; } = Role.User;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<UserRefreshToken> RefreshTokens { get; } = [];
    public ICollection<Product> CreatedProducts { get; } = [];
    public ICollection<Course> Courses { get; } = [];
    public ICollection<IntakeLog> IntakeLogs { get; } = [];
}