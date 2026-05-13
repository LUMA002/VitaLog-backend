using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using VitaLog.Api.Domain.Enums;
using VitaLog.Api.Infrastructure.Auth;
using VitaLog.Api.Infrastructure.Validation;

namespace VitaLog.Api.Features.Sync;

public static class SyncEndpoint
{
    public static RouteHandlerBuilder MapSyncEndpoint(this RouteGroupBuilder group)
    {
        return group.MapPost("/sync", static async Task<Ok<SyncResponse>> (
            [FromBody] SyncRequest request,
            HttpContext context,
            SyncHandler syncHandler,
            CancellationToken ct) =>
        {
            var userId = context.GetCurrentUserId();
            var response = await syncHandler.HandleAsync(userId, request, ct);
            return TypedResults.Ok(response);
        })
        .WithName("SyncOfflineData")
        .WithSummary("Bi-directional synchronization for offline-first clients")
        .RequireAuthorization()
        .AddValidationFilter<SyncRequest>();
    }
}

public static class SyncLimits
{
    public const int MaxItemsPerEntity = 2000;
    public const int MaxClockSkewMinutes = 5;
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(MaxClockSkewMinutes);
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

public sealed record SyncGlobalIngredientDto(
    Guid Id,
    string Name,
    string DefaultUnit,
    IngredientCategory Category,
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
    IReadOnlyList<SyncIntakeLogDto> IntakeLogs,
    IReadOnlyList<SyncGlobalIngredientDto> GlobalIngredients);

public sealed class SyncRequestValidator : AbstractValidator<SyncRequest>
{
    private const string UtcOffsetMessage = "must use UTC (offset +00:00).";
    private const string UpdatedAtUtcMessage = $"UpdatedAt {UtcOffsetMessage}";
    private const string DeletedAtUtcMessage = $"DeletedAt {UtcOffsetMessage} when provided.";

    private readonly TimeProvider _timeProvider;

    public SyncRequestValidator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        RuleFor(static x => x.LastSyncAt)
            .Must(static lastSyncAt => lastSyncAt is null || IsUtc(lastSyncAt.Value))
            .WithMessage($"LastSyncAt {UtcOffsetMessage}")
            .Must(NotBeInFuture)
            .WithMessage("LastSyncAt cannot be in the future.");

        RuleFor(static x => x.ClientTime)
            .Must(IsUtc)
            .WithMessage($"ClientTime {UtcOffsetMessage}")
            .Must(BeWithinAllowedClockSkew)
            .WithMessage($"ClientTime must be within +/- {SyncLimits.MaxClockSkewMinutes} minutes from server time.");

        RuleFor(static x => x.Products)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(static products => products.Count <= SyncLimits.MaxItemsPerEntity)
            .WithMessage($"Products cannot contain more than {SyncLimits.MaxItemsPerEntity} items.")
            .Must(static products => HaveDistinctIds(products.Select(static x => x.Id)))
            .WithMessage("Products must not contain duplicate Id values.");

        RuleFor(static x => x.ProductIngredients)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(static items => items.Count <= SyncLimits.MaxItemsPerEntity)
            .WithMessage($"ProductIngredients cannot contain more than {SyncLimits.MaxItemsPerEntity} items.")
            .Must(static items => HaveDistinctIds(items.Select(static x => x.Id)))
            .WithMessage("ProductIngredients must not contain duplicate Id values.");

        RuleFor(static x => x.Courses)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(static courses => courses.Count <= SyncLimits.MaxItemsPerEntity)
            .WithMessage($"Courses cannot contain more than {SyncLimits.MaxItemsPerEntity} items.")
            .Must(static courses => HaveDistinctIds(courses.Select(static x => x.Id)))
            .WithMessage("Courses must not contain duplicate Id values.");

        RuleFor(static x => x.IntakeLogs)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(static logs => logs.Count <= SyncLimits.MaxItemsPerEntity)
            .WithMessage($"IntakeLogs cannot contain more than {SyncLimits.MaxItemsPerEntity} items.")
            .Must(static logs => HaveDistinctIds(logs.Select(static x => x.Id)))
            .WithMessage("IntakeLogs must not contain duplicate Id values.");

        RuleForEach(static x => x.Products)
            .ChildRules(product =>
            {
                product.RuleFor(static x => x.Id)
                    .NotEmpty();

                product.RuleFor(static x => x.Name)
                    .NotEmpty()
                    .MaximumLength(200);

                product.RuleFor(static x => x.Description)
                    .MaximumLength(2000);

                product.RuleFor(static x => x.UpdatedAt)
                    .Must(IsUtc)
                    .WithMessage(UpdatedAtUtcMessage)
                    .Must(NotTooFarInFuture)
                    .WithMessage($"UpdatedAt cannot be more than {SyncLimits.MaxClockSkewMinutes} minutes in the future.");

                product.RuleFor(static x => x.DeletedAt)
                    .Must(static deletedAt => deletedAt is null || IsUtc(deletedAt.Value))
                    .WithMessage(DeletedAtUtcMessage);
            });

        RuleForEach(static x => x.ProductIngredients)
            .ChildRules(productIngredient =>
            {
                productIngredient.RuleFor(static x => x.Id)
                    .NotEmpty();

                productIngredient.RuleFor(static x => x.ProductId)
                    .NotEmpty();

                productIngredient.RuleFor(static x => x.IngredientId)
                    .Must(static ingredientId => ingredientId is null || ingredientId.Value != Guid.Empty)
                    .WithMessage("IngredientId cannot be an empty Guid.");

                productIngredient.RuleFor(static x => x.CustomIngredientName)
                    .MaximumLength(200);

                productIngredient.RuleFor(static x => x)
                    .Must(static x => HasValidIngredientHybrid(x.IngredientId, x.CustomIngredientName))
                    .WithMessage("Exactly one of IngredientId or CustomIngredientName must be provided.");

                productIngredient.RuleFor(static x => x.Amount)
                    .GreaterThan(0m);

                productIngredient.RuleFor(static x => x.Unit)
                    .NotEmpty()
                    .MaximumLength(50);

                productIngredient.RuleFor(static x => x.UpdatedAt)
                    .Must(IsUtc)
                    .WithMessage(UpdatedAtUtcMessage)
                    .Must(NotTooFarInFuture)
                    .WithMessage($"UpdatedAt cannot be more than {SyncLimits.MaxClockSkewMinutes} minutes in the future.");

                productIngredient.RuleFor(static x => x.DeletedAt)
                    .Must(static deletedAt => deletedAt is null || IsUtc(deletedAt.Value))
                    .WithMessage(DeletedAtUtcMessage);
            });

        RuleForEach(static x => x.Courses)
            .ChildRules(course =>
            {
                course.RuleFor(static x => x.Id)
                    .NotEmpty();

                course.RuleFor(static x => x.ProductId)
                    .NotEmpty();

                course.RuleFor(static x => x.ServingSize)
                    .GreaterThan(0m);

                course.RuleFor(static x => x)
                    .Must(static x => x.EndDate is null || x.EndDate >= x.StartDate)
                    .WithMessage("EndDate must be greater than or equal to StartDate.");

                course.RuleFor(static x => x.UpdatedAt)
                    .Must(IsUtc)
                    .WithMessage(UpdatedAtUtcMessage)
                    .Must(NotTooFarInFuture)
                    .WithMessage($"UpdatedAt cannot be more than {SyncLimits.MaxClockSkewMinutes} minutes in the future.");

                course.RuleFor(static x => x.DeletedAt)
                    .Must(static deletedAt => deletedAt is null || IsUtc(deletedAt.Value))
                    .WithMessage(DeletedAtUtcMessage);
            });

        RuleForEach(static x => x.IntakeLogs)
            .ChildRules(log =>
            {
                log.RuleFor(static x => x.Id)
                    .NotEmpty();

                log.RuleFor(static x => x.CourseId)
                    .NotEmpty();

                log.RuleFor(static x => x.ActualServingSize)
                    .GreaterThan(0m);

                log.RuleFor(static x => x.TakenAt)
                    .Must(IsUtc)
                    .WithMessage($"TakenAt {UtcOffsetMessage}");

                log.RuleFor(static x => x.UpdatedAt)
                    .Must(IsUtc)
                    .WithMessage(UpdatedAtUtcMessage)
                    .Must(NotTooFarInFuture)
                    .WithMessage($"UpdatedAt cannot be more than {SyncLimits.MaxClockSkewMinutes} minutes in the future.");

                log.RuleFor(static x => x.DeletedAt)
                    .Must(static deletedAt => deletedAt is null || IsUtc(deletedAt.Value))
                    .WithMessage(DeletedAtUtcMessage);
            });
    }

    private bool BeWithinAllowedClockSkew(DateTimeOffset clientTime)
    {
        var now = _timeProvider.GetUtcNow();
        return (clientTime - now).Duration() <= SyncLimits.MaxClockSkew;
    }

    private bool NotBeInFuture(DateTimeOffset? value)
    {
        return value is null || value.Value <= _timeProvider.GetUtcNow();
    }

    private bool NotTooFarInFuture(DateTimeOffset value)
    {
        return value <= _timeProvider.GetUtcNow().Add(SyncLimits.MaxClockSkew);
    }

    private static bool IsUtc(DateTimeOffset value)
    {
        return value.Offset == TimeSpan.Zero;
    }

    private static bool HaveDistinctIds(IEnumerable<Guid> ids)
    {
        // use TryGetNonEnumeratedCount to avoid allocation if the collection is already enumerated
        var capacity = ids.TryGetNonEnumeratedCount(out var count) ? count : 0;
        var uniqueIds = new HashSet<Guid>(capacity);

    // avoid allocation of the delegate (instead of using LINQ .All)
    foreach (var id in ids)
    {
        if (!uniqueIds.Add(id))
        {
            return false;
        }
    }

    return true;
    }

    private static bool HasValidIngredientHybrid(Guid? ingredientId, string? customIngredientName)
    {
        var hasIngredientId = ingredientId is Guid value && value != Guid.Empty;
        var hasCustomName = !string.IsNullOrWhiteSpace(customIngredientName);

        return hasIngredientId ^ hasCustomName; // guaranteed that only one of the two is provided (global ingredient or custom ingredient name)
    }
}