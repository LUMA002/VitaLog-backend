using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database.Configurations;

public sealed class ProductIngredientConfiguration : IEntityTypeConfiguration<ProductIngredient>
{
    public void Configure(EntityTypeBuilder<ProductIngredient> builder)
    {
        builder.ToTable("ProductIngredients", t =>
        {
            t.HasCheckConstraint(
                "CK_ProductIngredients_Hybrid_IngredientOrCustom",
                """
                (
                    ("IngredientId" IS NOT NULL AND "CustomIngredientName" IS NULL)
                    OR
                    ("IngredientId" IS NULL AND "CustomIngredientName" IS NOT NULL AND length(trim("CustomIngredientName")) > 0)
                )
                """);

            t.HasCheckConstraint(
                "CK_ProductIngredients_Amount_Positive",
                "\"Amount\" > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CustomIngredientName)
            .HasMaxLength(200);

        builder.Property(x => x.Amount)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.Unit)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_ProductIngredients_UpdatedAt");

        builder.HasIndex(x => new { x.ProductId, x.UpdatedAt })
            .HasDatabaseName("IX_ProductIngredients_ProductId_UpdatedAt");

        builder.HasIndex(x => new { x.ProductId, x.IngredientId })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL AND \"IngredientId\" IS NOT NULL")
            .HasDatabaseName("UX_ProductIngredients_Product_Ingredient_Active");

        builder.HasIndex(x => new { x.ProductId, x.CustomIngredientName })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL AND \"IngredientId\" IS NULL AND \"CustomIngredientName\" IS NOT NULL")
            .HasDatabaseName("UX_ProductIngredients_Product_CustomIngredient_Active");

        builder.HasOne(x => x.Product)
            .WithMany(x => x.Ingredients)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Ingredient)
            .WithMany(x => x.ProductIngredients)
            .HasForeignKey(x => x.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}