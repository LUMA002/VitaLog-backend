using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        // Intentionally NOT unique because of business decision for offline-first sync safety and user experience.
        // Frontend will handle duplicate names.
        builder.HasIndex(x => x.Name)
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_Products_Name_Active");

        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_Products_UpdatedAt");

        builder.HasIndex(x => new { x.CreatorUserId, x.UpdatedAt })
            .HasDatabaseName("IX_Products_CreatorUserId_UpdatedAt");

        builder.HasOne(x => x.CreatorUser)
            .WithMany(x => x.CreatedProducts)
            .HasForeignKey(x => x.CreatorUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}