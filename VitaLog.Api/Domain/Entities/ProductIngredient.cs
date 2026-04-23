namespace VitaLog.Api.Domain.Entities;

public sealed class ProductIngredient
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? IngredientId { get; set; } // nullable by hybrid rule
    public string? CustomIngredientName { get; set; } // nullable by hybrid rule
    public decimal Amount { get; set; } // > 0
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public Product Product { get; set; } = null!;
    public GlobalIngredient? Ingredient { get; set; }
}