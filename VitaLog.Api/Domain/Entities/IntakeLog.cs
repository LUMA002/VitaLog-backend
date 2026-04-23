namespace VitaLog.Api.Domain.Entities;

public sealed class IntakeLog
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; } // denormalized for fast sync
    public decimal ActualServingSize { get; set; } // snapshot, > 0
    public DateTimeOffset TakenAt { get; set; } // UTC
    public DateTimeOffset UpdatedAt { get; set; } // UTC
    public DateTimeOffset? DeletedAt { get; set; }

    public Course Course { get; set; } = null!;
    public User User { get; set; } = null!;
}