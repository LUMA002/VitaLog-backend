using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database.Configurations;

public sealed class IntakeLogConfiguration : IEntityTypeConfiguration<IntakeLog>
{
    public void Configure(EntityTypeBuilder<IntakeLog> builder)
    {
        builder.ToTable("IntakeLogs", t =>
        {
            t.HasCheckConstraint(
                "CK_IntakeLogs_ActualServingSize_Positive",
                "\"ActualServingSize\" > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ActualServingSize)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.TakenAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        // Sync indexes
        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_IntakeLogs_UpdatedAt");

        builder.HasIndex(x => new { x.UserId, x.UpdatedAt })
            .HasDatabaseName("IX_IntakeLogs_UserId_UpdatedAt");

        // Active history read indexes (soft-delete aware)
        builder.HasIndex(x => new { x.UserId, x.TakenAt })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_IntakeLogs_UserId_TakenAt_Active");

        builder.HasIndex(x => new { x.CourseId, x.TakenAt })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_IntakeLogs_CourseId_TakenAt_Active");

        // Isolated FK for denormalized user ownership
        builder.HasOne(x => x.User)
            .WithMany(x => x.IntakeLogs)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Isolated FK for course relation
        builder.HasOne(x => x.Course)
            .WithMany(x => x.IntakeLogs)
            .HasForeignKey(x => x.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}