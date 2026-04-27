using VitaLog.Api.Domain.Enums;

namespace VitaLog.Api.Domain.Entities;

public sealed class GlobalIngredient
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public IngredientCategory Category { get; set; }
    public string DefaultUnit { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<ProductIngredient> ProductIngredients { get; } = [];
}