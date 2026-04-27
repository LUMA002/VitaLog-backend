using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Enums;
using VitaLog.Api.Infrastructure.Database;

namespace VitaLog.Api.Features.Directory;

public sealed record IngredientResponse(Guid Id, string Name, string DefaultUnit, IngredientCategory Category);

public static class GetIngredientsEndpoint
{
    public static RouteHandlerBuilder MapGetIngredients(this RouteGroupBuilder group)
    {
        return group.MapGet("/ingredients", static async Task<Ok<List<IngredientResponse>>> (
            [FromServices] AppDbContext db, // We also can do not write [FromServices] for recognition
            CancellationToken ct) =>
        {
            var list = await db.GlobalIngredients
                .AsNoTracking()
                .Where(static x => x.DeletedAt == null)
                .OrderBy(static x => x.Name)
                .Select(static x => new IngredientResponse(
                    x.Id,
                    x.Name,
                    x.DefaultUnit,
                    x.Category))
                // x.Category.ToString()))
                .ToListAsync(ct);

            return TypedResults.Ok(list);
        })
        .WithName("GetIngredients")
        .WithSummary("Get active global ingredients")
        .AllowAnonymous();
    }
}
