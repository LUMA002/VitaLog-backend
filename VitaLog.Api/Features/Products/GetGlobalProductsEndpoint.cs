using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Infrastructure.Database;

namespace VitaLog.Api.Features.Products;

public sealed record GlobalProductIngredientDto(Guid IngredientId, string Name, decimal Amount, string Unit);
public sealed record GlobalProductResponse(Guid Id, string Name, string? Description, List<GlobalProductIngredientDto> Ingredients);

public static class GetGlobalProductsEndpoint
{
    public static RouteHandlerBuilder MapGetGlobalProducts(this RouteGroupBuilder group)
    {
        return group.MapGet("/products/global", static async Task<Ok<List<GlobalProductResponse>>> (
            [FromServices] AppDbContext db,
            CancellationToken ct) =>
        {
            var list = await db.Products
                .AsNoTracking()
                .Where(static p => p.CreatorUserId == null && p.DeletedAt == null)
                .OrderBy(static p => p.Name)
                .Select(static p => new GlobalProductResponse(
                    p.Id,
                    p.Name,
                    p.Description,
                    p.Ingredients
                        .Where(static pi => pi.DeletedAt == null && pi.IngredientId != null && pi.Ingredient != null)
                        .OrderBy(static pi => pi.Ingredient!.Name)
                        .Select(static pi => new GlobalProductIngredientDto(
                            pi.IngredientId!.Value,
                            pi.Ingredient!.Name,
                            pi.Amount,
                            pi.Unit))
                        .ToList()))
                .ToListAsync(ct);

            return TypedResults.Ok(list);
        })
        .WithName("GetGlobalProducts")
        .WithSummary("Get active global products with ingredient details")
        .AllowAnonymous();
    }
}
