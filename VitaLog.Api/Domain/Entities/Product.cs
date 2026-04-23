namespace VitaLog.Api.Domain.Entities;

public sealed class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CreatorUserId { get; set; } // null = global product
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? CreatorUser { get; set; }
    public ICollection<ProductIngredient> Ingredients { get; set; } = [];
    public ICollection<Course> Courses { get; set; } = [];
}