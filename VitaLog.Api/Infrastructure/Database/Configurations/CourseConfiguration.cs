using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database.Configurations;

public sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses", t =>
        {
            t.HasCheckConstraint(
                "CK_Courses_ServingSize_Positive",
                "\"ServingSize\" > 0");

            t.HasCheckConstraint(
                "CK_Courses_EndDate_AfterOrEqual_StartDate",
                "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ServingSize)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.TimeOfDay)
            .IsRequired()
            .HasColumnType("time");

        builder.Property(x => x.StartDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(x => x.EndDate)
            .HasColumnType("date");

        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);


        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_Courses_UpdatedAt");

        builder.HasIndex(x => new { x.UserId, x.UpdatedAt })
            .HasDatabaseName("IX_Courses_UserId_UpdatedAt");

        builder.HasIndex(x => new { x.ProductId, x.UpdatedAt })
            .HasDatabaseName("IX_Courses_ProductId_UpdatedAt");

        builder.HasOne(x => x.User)
            .WithMany(x => x.Courses)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany(x => x.Courses)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}