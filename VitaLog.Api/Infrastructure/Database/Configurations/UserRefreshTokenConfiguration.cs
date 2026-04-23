using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database.Configurations;

public sealed class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.ToTable("UserRefreshTokens", t =>
        {
            t.HasCheckConstraint(
                "CK_UserRefreshTokens_ExpiresAt_After_CreatedAt",
                "\"ExpiresAt\" > \"CreatedAt\"");

            t.HasCheckConstraint(
                "CK_UserRefreshTokens_RevokedAt_After_CreatedAt",
                "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Token)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RevokedAt);
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.Token)
            .IsUnique()
            .HasDatabaseName("UX_UserRefreshTokens_Token");

        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_UserRefreshTokens_UpdatedAt");

        builder.HasIndex(x => new { x.UserId, x.RevokedAt, x.ExpiresAt })
            .HasDatabaseName("IX_UserRefreshTokens_UserId_RevokedAt_ExpiresAt");

        builder.HasOne(x => x.User)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}