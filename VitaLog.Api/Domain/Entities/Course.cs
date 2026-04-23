namespace VitaLog.Api.Domain.Entities;

public sealed class Course
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public decimal ServingSize { get; set; } // > 0
    public TimeOnly TimeOfDay { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; } // null = infinite
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ICollection<IntakeLog> IntakeLogs { get; set; } = [];
}