using System.Data;
using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Infrastructure.Database;

namespace VitaLog.Api.Features.Sync;

public sealed class SyncHandler(AppDbContext db, TimeProvider timeProvider)
{
    private const string TakeoverAttemptMessage = "Takeover attempt detected.";

    public async Task<SyncResponse> HandleAsync(Guid userId, SyncRequest request, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var serverNow = timeProvider.GetUtcNow();

            await UpsertProductsAsync(userId, request.Products, serverNow, ct);
            await UpsertProductIngredientsAsync(userId, request.ProductIngredients, serverNow, ct);
            await UpsertCoursesAsync(userId, request.Courses, serverNow, ct);
            await UpsertIntakeLogsAsync(userId, request.IntakeLogs, serverNow, ct);

            await db.SaveChangesAsync(ct);

            var since = request.LastSyncAt?.AddSeconds(-1) ?? DateTimeOffset.MinValue;

            var products = await db.Products
                .AsNoTracking()
                .Where(static x => x.UpdatedAt > DateTimeOffset.MinValue)
                .Where(x => x.UpdatedAt > since && (x.CreatorUserId == userId || x.CreatorUserId == null))
                .Select(static x => new SyncProductDto(
                    x.Id,
                    x.Name,
                    x.Description,
                    x.UpdatedAt,
                    x.DeletedAt,
                    x.CreatorUserId))
                .ToListAsync(ct);

            var productIngredients = await db.ProductIngredients
                .AsNoTracking()
                .Where(x => x.UpdatedAt > since && (x.Product.CreatorUserId == userId || x.Product.CreatorUserId == null))
                .Select(static x => new SyncProductIngredientDto(
                    x.Id,
                    x.ProductId,
                    x.IngredientId,
                    x.CustomIngredientName,
                    x.Amount,
                    x.Unit,
                    x.UpdatedAt,
                    x.DeletedAt))
                .ToListAsync(ct);

            var courses = await db.Courses
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.UpdatedAt > since)
                .Select(static x => new SyncCourseDto(
                    x.Id,
                    x.ProductId,
                    x.ServingSize,
                    x.TimeOfDay,
                    x.StartDate,
                    x.EndDate,
                    x.UpdatedAt,
                    x.DeletedAt))
                .ToListAsync(ct);

            var intakeLogs = await db.IntakeLogs
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.UpdatedAt > since)
                .Select(static x => new SyncIntakeLogDto(
                    x.Id,
                    x.CourseId,
                    x.ActualServingSize,
                    x.TakenAt,
                    x.UpdatedAt,
                    x.DeletedAt))
                .ToListAsync(ct);

            var globalIngredients = await db.GlobalIngredients
                .AsNoTracking()
                .Where(x => x.UpdatedAt > since)
                .Select(static x => new SyncGlobalIngredientDto(
                    x.Id,
                    x.Name,
                    x.DefaultUnit,
                    x.Category,
                    x.UpdatedAt,
                    x.DeletedAt))
                .ToListAsync(ct);

            var response = new SyncResponse(
                serverNow,
                products,
                productIngredients,
                courses,
                intakeLogs,
                globalIngredients);

            await tx.CommitAsync(ct);
            return response;
        });
    }

    private async Task UpsertProductsAsync(
        Guid userId,
        IReadOnlyList<SyncProductDto> incomingProducts,
        DateTimeOffset serverNow,
        CancellationToken ct)
    {
        if (incomingProducts.Count == 0)
        {
            return;
        }

        var ids = incomingProducts.Select(static x => x.Id).ToArray();
        var existingById = await db.Products
            .Where(x => ids.Contains(x.Id)) // translating to SQL (in PostgreSQL) in smth like WHERE "Id" = ANY(@p0), where @p0 is an array of GUIDs, which is efficient even for large lists of IDs
            .ToDictionaryAsync(static x => x.Id, ct);

        foreach (var dto in incomingProducts)
        {
            if (existingById.TryGetValue(dto.Id, out var existing))
            {
                if (existing.CreatorUserId != userId)
                {
                    throw new UnauthorizedAccessException(TakeoverAttemptMessage);
                }

                if (dto.UpdatedAt > existing.UpdatedAt)
                {
                    existing.Name = dto.Name;
                    existing.Description = dto.Description;
                    existing.DeletedAt = dto.DeletedAt;
                    existing.UpdatedAt = serverNow;
                }

                continue;
            }

            db.Products.Add(new Product
            {
                Id = dto.Id,
                Name = dto.Name,
                Description = dto.Description,
                CreatorUserId = userId,
                UpdatedAt = serverNow,
                DeletedAt = dto.DeletedAt
            });
        }
    }

    private async Task UpsertProductIngredientsAsync(
        Guid userId,
        IReadOnlyList<SyncProductIngredientDto> incomingProductIngredients,
        DateTimeOffset serverNow,
        CancellationToken ct)
    {
        if (incomingProductIngredients.Count == 0)
        {
            return;
        }

        var ids = incomingProductIngredients.Select(static x => x.Id).ToArray();
        var existingById = await db.ProductIngredients
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(static x => x.Id, ct);

        var productIds = incomingProductIngredients
            .Select(static x => x.ProductId)
            .Concat(existingById.Values.Select(static x => x.ProductId))
            .Distinct()
            .ToArray();

        var productOwnersById = await db.Products
            .Where(x => productIds.Contains(x.Id))
            .Select(static x => new { x.Id, x.CreatorUserId })
            .ToDictionaryAsync(static x => x.Id, static x => x.CreatorUserId, ct);

        MergePendingAddedProductOwners(db, productIds, productOwnersById);

        foreach (var dto in incomingProductIngredients)
        {
            if (!productOwnersById.TryGetValue(dto.ProductId, out var productOwnerId))
            {
                throw new InvalidOperationException("Product for ProductIngredient does not exist.");
            }

            if (productOwnerId != userId)
            {
                throw new UnauthorizedAccessException(TakeoverAttemptMessage);
            }

            if (existingById.TryGetValue(dto.Id, out var existing))
            {
                if (existing.ProductId != dto.ProductId)
                {
                    throw new UnauthorizedAccessException(TakeoverAttemptMessage);
                }

                if (dto.UpdatedAt > existing.UpdatedAt)
                {
                    existing.IngredientId = dto.IngredientId;
                    existing.CustomIngredientName = dto.CustomIngredientName;
                    existing.Amount = dto.Amount;
                    existing.Unit = dto.Unit;
                    existing.DeletedAt = dto.DeletedAt;
                    existing.UpdatedAt = serverNow;
                }

                continue;
            }

            db.ProductIngredients.Add(new ProductIngredient
            {
                Id = dto.Id,
                ProductId = dto.ProductId,
                IngredientId = dto.IngredientId,
                CustomIngredientName = dto.CustomIngredientName,
                Amount = dto.Amount,
                Unit = dto.Unit,
                UpdatedAt = serverNow,
                DeletedAt = dto.DeletedAt
            });
        }
    }

    private async Task UpsertCoursesAsync(
        Guid userId,
        IReadOnlyList<SyncCourseDto> incomingCourses,
        DateTimeOffset serverNow,
        CancellationToken ct)
    {
        if (incomingCourses.Count == 0)
        {
            return;
        }

        var ids = incomingCourses.Select(static x => x.Id).ToArray();
        var existingById = await db.Courses
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(static x => x.Id, ct);

        var productIds = incomingCourses
            .Select(static x => x.ProductId)
            .Concat(existingById.Values.Select(static x => x.ProductId))
            .Distinct()
            .ToArray();

        var productOwnersById = await db.Products
            .Where(x => productIds.Contains(x.Id))
            .Select(static x => new { x.Id, x.CreatorUserId })
            .ToDictionaryAsync(static x => x.Id, static x => x.CreatorUserId, ct);

        MergePendingAddedProductOwners(db, productIds, productOwnersById);

        foreach (var dto in incomingCourses)
        {
            if (!productOwnersById.TryGetValue(dto.ProductId, out var productOwnerId))
            {
                throw new InvalidOperationException("Product for Course does not exist.");
            }

            if (productOwnerId is Guid ownerId && ownerId != userId)
            {
                throw new UnauthorizedAccessException(TakeoverAttemptMessage);
            }

            if (existingById.TryGetValue(dto.Id, out var existing))
            {
                if (existing.UserId != userId)
                {
                    throw new UnauthorizedAccessException(TakeoverAttemptMessage);
                }

                if (dto.UpdatedAt > existing.UpdatedAt)
                {
                    existing.ProductId = dto.ProductId;
                    existing.ServingSize = dto.ServingSize;
                    existing.TimeOfDay = dto.TimeOfDay;
                    existing.StartDate = dto.StartDate;
                    existing.EndDate = dto.EndDate;
                    existing.DeletedAt = dto.DeletedAt;
                    existing.UpdatedAt = serverNow;
                }

                continue;
            }

            db.Courses.Add(new Course
            {
                Id = dto.Id,
                UserId = userId,
                ProductId = dto.ProductId,
                ServingSize = dto.ServingSize,
                TimeOfDay = dto.TimeOfDay,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                UpdatedAt = serverNow,
                DeletedAt = dto.DeletedAt
            });
        }
    }

    private async Task UpsertIntakeLogsAsync(
        Guid userId,
        IReadOnlyList<SyncIntakeLogDto> incomingIntakeLogs,
        DateTimeOffset serverNow,
        CancellationToken ct)
    {
        if (incomingIntakeLogs.Count == 0)
        {
            return;
        }

        var ids = incomingIntakeLogs.Select(static x => x.Id).ToArray();
        var existingById = await db.IntakeLogs
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(static x => x.Id, ct);

        var courseIds = incomingIntakeLogs
            .Select(static x => x.CourseId)
            .Concat(existingById.Values.Select(static x => x.CourseId))
            .Distinct()
            .ToArray();

        var courseOwnersById = await db.Courses
            .Where(x => courseIds.Contains(x.Id))
            .Select(static x => new { x.Id, x.UserId })
            .ToDictionaryAsync(static x => x.Id, static x => x.UserId, ct);

        MergePendingAddedCourseOwners(db, courseIds, courseOwnersById);

        foreach (var dto in incomingIntakeLogs)
        {
            if (!courseOwnersById.TryGetValue(dto.CourseId, out var courseOwnerId))
            {
                throw new InvalidOperationException("Course for IntakeLog does not exist.");
            }

            if (courseOwnerId != userId)
            {
                throw new UnauthorizedAccessException(TakeoverAttemptMessage);
            }

            if (existingById.TryGetValue(dto.Id, out var existing))
            {
                if (existing.UserId != userId || existing.CourseId != dto.CourseId)
                {
                    throw new UnauthorizedAccessException(TakeoverAttemptMessage);
                }

                if (dto.UpdatedAt > existing.UpdatedAt)
                {
                    existing.ActualServingSize = dto.ActualServingSize;
                    existing.TakenAt = dto.TakenAt;
                    existing.DeletedAt = dto.DeletedAt;
                    existing.UpdatedAt = serverNow;
                }

                continue;
            }

            db.IntakeLogs.Add(new IntakeLog
            {
                Id = dto.Id,
                CourseId = dto.CourseId,
                UserId = userId,
                ActualServingSize = dto.ActualServingSize,
                TakenAt = dto.TakenAt,
                UpdatedAt = serverNow,
                DeletedAt = dto.DeletedAt
            });
        }
    }

    /// <summary>
    /// EF queries do not see <see cref="EntityState.Added"/> rows until SaveChanges.
    /// Same-batch FK checks (Product -> ProductIngredient -> Course) must still resolve ownership.
    /// </summary>
    private static void MergePendingAddedProductOwners(
        AppDbContext db,
        IEnumerable<Guid> productIds,
        Dictionary<Guid, Guid?> ownersByProductId)
    {
        var wanted = productIds as IReadOnlySet<Guid> ?? productIds.ToHashSet();
        foreach (var entry in db.ChangeTracker.Entries<Product>())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (!wanted.Contains(entry.Entity.Id))
            {
                continue;
            }

            ownersByProductId[entry.Entity.Id] = entry.Entity.CreatorUserId;
        }
    }

    private static void MergePendingAddedCourseOwners(
        AppDbContext db,
        IEnumerable<Guid> courseIds,
        Dictionary<Guid, Guid> ownersByCourseId)
    {
        var wanted = courseIds as IReadOnlySet<Guid> ?? courseIds.ToHashSet();
        foreach (var entry in db.ChangeTracker.Entries<Course>())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (!wanted.Contains(entry.Entity.Id))
            {
                continue;
            }

            ownersByCourseId[entry.Entity.Id] = entry.Entity.UserId;
        }
    }
}
