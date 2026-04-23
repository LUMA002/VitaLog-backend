using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.PasswordHash)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(x => x.Roles)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        // Email uniqueness for active users only.
        // Case normalization is handled (must be) in C# business logic.
        builder.HasIndex(x => x.Email)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_Users_Email_Active");

        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_Users_UpdatedAt");
    }
}