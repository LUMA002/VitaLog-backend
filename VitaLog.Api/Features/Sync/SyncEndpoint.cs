using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace VitaLog.Api.Features.Sync;

public static class SyncEndpoint
{
    public static RouteHandlerBuilder MapSyncEndpoint(this RouteGroupBuilder group)
    {
        return group.MapPost("/sync", static Ok<SyncResponse> (
            [FromBody] SyncRequest request,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var response = new SyncResponse(
                timeProvider.GetUtcNow(),
                [],
                [],
                [],
                []
            );

            return TypedResults.Ok(response);
        })
        .WithName("SyncOfflineData")
        .WithSummary("Bi-directional synchronization for offline-first clients");
    }
}

public sealed record SyncProductDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SyncProductIngredientDto(
    Guid Id,
    Guid ProductId,
    Guid? IngredientId,
    string? CustomIngredientName,
    decimal Amount,
    string Unit,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SyncCourseDto(
    Guid Id,
    Guid ProductId,
    decimal ServingSize,
    TimeOnly TimeOfDay,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SyncIntakeLogDto(
    Guid Id,
    Guid CourseId,
    decimal ActualServingSize,
    DateTimeOffset TakenAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SyncRequest(
    DateTimeOffset? LastSyncAt,
    DateTimeOffset ClientTime,
    IReadOnlyList<SyncProductDto> Products,
    IReadOnlyList<SyncProductIngredientDto> ProductIngredients,
    IReadOnlyList<SyncCourseDto> Courses,
    IReadOnlyList<SyncIntakeLogDto> IntakeLogs);

public sealed record SyncResponse(
    DateTimeOffset ServerTime,
    IReadOnlyList<SyncProductDto> Products,
    IReadOnlyList<SyncProductIngredientDto> ProductIngredients,
    IReadOnlyList<SyncCourseDto> Courses,
    IReadOnlyList<SyncIntakeLogDto> IntakeLogs);