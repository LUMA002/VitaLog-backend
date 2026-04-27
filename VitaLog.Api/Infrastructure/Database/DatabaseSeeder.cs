using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Domain.Enums;

namespace VitaLog.Api.Infrastructure.Database;

public sealed class DatabaseSeeder(AppDbContext db, TimeProvider timeProvider, ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.GlobalIngredients.AnyAsync(ct))
        {
            logger.LogInformation("Database already seeded");
            return;
        }

        var now = timeProvider.GetUtcNow();

        var vitaminC = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            Name = "Vitamin C",
            Category = IngredientCategory.Vitamin,
            DefaultUnit = "mg",
            UpdatedAt = now
        };

        var vitaminD3 = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            Name = "Vitamin D3",
            Category = IngredientCategory.Vitamin,
            DefaultUnit = "IU",
            UpdatedAt = now
        };

        var magnesium = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            Name = "Magnesium",
            Category = IngredientCategory.Mineral,
            DefaultUnit = "mg",
            UpdatedAt = now
        };

        var zinc = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            Name = "Zinc",
            Category = IngredientCategory.Mineral,
            DefaultUnit = "mg",
            UpdatedAt = now
        };

        var omega3 = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            Name = "Omega-3",
            Category = IngredientCategory.Supplement,
            DefaultUnit = "mg",
            UpdatedAt = now
        };

        var lTheanine = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            Name = "L-Theanine",
            Category = IngredientCategory.Supplement,
            DefaultUnit = "mg",
            UpdatedAt = now
        };

        var dailyCoreComplex = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Daily Core Complex",
            Description = "Essential daily vitamins and minerals",
            CreatorUserId = null,
            UpdatedAt = now
        };

        var relaxAndRecovery = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Relax & Recovery",
            Description = "Evening support for nervous system",
            CreatorUserId = null,
            UpdatedAt = now
        };

        ProductIngredient[] productIngredients =
        [
            new ProductIngredient
            {
                Id = Guid.NewGuid(),
                ProductId = dailyCoreComplex.Id,
                IngredientId = vitaminC.Id,
                Amount = 500m,
                Unit = "mg",
                UpdatedAt = now
            },
            new ProductIngredient
            {
                Id = Guid.NewGuid(),
                ProductId = dailyCoreComplex.Id,
                IngredientId = vitaminD3.Id,
                Amount = 2000m,
                Unit = "IU",
                UpdatedAt = now
            },
            new ProductIngredient
            {
                Id = Guid.NewGuid(),
                ProductId = dailyCoreComplex.Id,
                IngredientId = zinc.Id,
                Amount = 15m,
                Unit = "mg",
                UpdatedAt = now
            },
            new ProductIngredient
            {
                Id = Guid.NewGuid(),
                ProductId = relaxAndRecovery.Id,
                IngredientId = magnesium.Id,
                Amount = 300m,
                Unit = "mg",
                UpdatedAt = now
            },
            new ProductIngredient
            {
                Id = Guid.NewGuid(),
                ProductId = relaxAndRecovery.Id,
                IngredientId = lTheanine.Id,
                Amount = 200m,
                Unit = "mg",
                UpdatedAt = now
            }
        ];

        db.AddRange(vitaminC, vitaminD3, magnesium, zinc, omega3, lTheanine);
        db.AddRange(dailyCoreComplex, relaxAndRecovery);
        db.AddRange(productIngredients);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Database seeded successfully");
    }
}
