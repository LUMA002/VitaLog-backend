using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database.Configurations;

public sealed class GlobalIngredientConfiguration : IEntityTypeConfiguration<GlobalIngredient>
{
    public void Configure(EntityTypeBuilder<GlobalIngredient> builder)
    {
        builder.ToTable("GlobalIngredients");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Category)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.DefaultUnit)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_GlobalIngredients_Name_Active");

        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_GlobalIngredients_UpdatedAt");
    }
}