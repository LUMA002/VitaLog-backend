using VitaLog.Api.Domain.Enums;

namespace VitaLog.Api.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Role Roles { get; set; } = Role.User;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<Product> CreatedProducts { get; set; } = [];
    public ICollection<Course> Courses { get; set; } = [];
    public ICollection<IntakeLog> IntakeLogs { get; set; } = [];
}