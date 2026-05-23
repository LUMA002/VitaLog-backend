using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Features.Auth;
using VitaLog.Api.Features.Sync;
using VitaLog.Api.Infrastructure.Database;
using VitaLog.Api.IntegrationTests.Infrastructure;

namespace VitaLog.Api.IntegrationTests.Features.Sync;

public sealed class SyncEndpointTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    private readonly CustomWebApplicationFactory _factory = factory;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Sync_WithoutJwt_ShouldReturnUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = CreateEmptySyncRequest();

        var response = await PostSyncRawAsync(request, accessToken: null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sync_FirstSyncWithEmptyPush_ShouldReturnGlobalAndUserData()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, accessToken) = await CreateAuthenticatedUserAsync(ct);

        var seeded = await SeedUserGraphAsync(userId, ct);
        var globalCount = await CountGlobalIngredientsAsync(ct);

        var response = await PostSyncAsync(CreateEmptySyncRequest(lastSyncAt: null), accessToken, ct);

        response.ServerTime.Should().BeAfter(DateTimeOffset.MinValue);
        response.GlobalIngredients.Should().HaveCount(globalCount);

        response.Products.Should().Contain(x => x.Id == seeded.ProductId);
        response.ProductIngredients.Should().Contain(x => x.Id == seeded.ProductIngredientId);
        response.Courses.Should().Contain(x => x.Id == seeded.CourseId);
        response.IntakeLogs.Should().Contain(x => x.Id == seeded.IntakeLogId);
    }

    [Fact]
    public async Task Sync_EmptyPushWithNoChangesSinceLastSync_ShouldReturnEmptyPayloads()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, accessToken) = await CreateAuthenticatedUserAsync(ct);

        var firstSync = await PostSyncAsync(CreateEmptySyncRequest(lastSyncAt: null), accessToken, ct);
        var secondSync = await PostSyncAsync(CreateEmptySyncRequest(firstSync.ServerTime), accessToken, ct);

        secondSync.ServerTime.Should().BeAfter(firstSync.ServerTime);
        secondSync.Products.Should().BeEmpty();
        secondSync.ProductIngredients.Should().BeEmpty();
        secondSync.Courses.Should().BeEmpty();
        secondSync.IntakeLogs.Should().BeEmpty();
        secondSync.GlobalIngredients.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_PushNewCourse_ShouldReturnAckWithServerTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);
        var courseId = Guid.NewGuid();

        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: courseId,
                    ProductId: globalProductId,
                    ServingSize: 2.0m,
                    TimeOfDay: new TimeOnly(8, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                    DeletedAt: null)
            ],
            IntakeLogs: []);

        var response = await PostSyncAsync(request, accessToken, ct);
        var echoedCourse = response.Courses.Single(x => x.Id == courseId);

        echoedCourse.UpdatedAt.Should().Be(response.ServerTime);

        var persisted = await FindCourseAsync(courseId, ct);
        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(userId);
        persisted.UpdatedAt.Should().Be(response.ServerTime);
    }

    [Fact]
    public async Task Sync_CrossUserIsolation_UserBShouldNotSeeUserACourse()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userAId, tokenA) = await CreateAuthenticatedUserAsync(ct);
        var (_, tokenB) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);

        var courseId = Guid.NewGuid();
        var pushFromUserA = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: courseId,
                    ProductId: globalProductId,
                    ServingSize: 1.0m,
                    TimeOfDay: new TimeOnly(9, 30),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
                    DeletedAt: null)
            ],
            IntakeLogs: []);

        var userASync = await PostSyncAsync(pushFromUserA, tokenA, ct);
        userASync.Courses.Should().Contain(x => x.Id == courseId);

        var userBSync = await PostSyncAsync(CreateEmptySyncRequest(lastSyncAt: null), tokenB, ct);
        userBSync.Courses.Should().NotContain(x => x.Id == courseId);

        var persisted = await FindCourseAsync(courseId, ct);
        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(userAId);
    }

    // Core LWW Conflict Resolution

    [Fact]
    public async Task Sync_LwwStalePush_ShouldIgnoreAndReturnCurrentDbValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var seeded = await SeedUserGraphAsync(userId, ct);

        // Push the same course with an OLDER timestamp (stale) and a different ServingSize
        // that must NOT be applied to the DB
        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: seeded.CourseId,
                    ProductId: seeded.ProductId,
                    ServingSize: 99.9m,
                    TimeOfDay: new TimeOnly(0, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-20), // older than T-10min seeded in DB
                    DeletedAt: null)
            ],
            IntakeLogs: []);

        var response = await PostSyncAsync(request, accessToken, ct);

        // Pull must return the original DB value, not the stale push value
        var echoedCourse = response.Courses.Should().ContainSingle(x => x.Id == seeded.CourseId).Subject;
        echoedCourse.ServingSize.Should().Be(1.5m);
        echoedCourse.UpdatedAt.Should().NotBe(response.ServerTime); // server did NOT re-stamp it

        // DB row must be unchanged
        var db = await FindCourseAsync(seeded.CourseId, ct);
        db.Should().NotBeNull();
        db!.ServingSize.Should().Be(1.5m);
        db.UpdatedAt.Should().NotBe(response.ServerTime);
    }

    [Fact]
    public async Task Sync_LwwFreshPush_ShouldUpdateDbAndStampWithServerTime()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var seeded = await SeedUserGraphAsync(userId, ct);

        // Push the same course with a NEWER timestamp — should win the LWW race
        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: seeded.CourseId,
                    ProductId: seeded.ProductId,
                    ServingSize: 3.0m,
                    TimeOfDay: new TimeOnly(0, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1), // newer than T-10min seeded in DB
                    DeletedAt: null)
            ],
            IntakeLogs: []);

        var response = await PostSyncAsync(request, accessToken, ct);

        // Response must echo the updated value stamped with serverNow
        var echoedCourse = response.Courses.Should().ContainSingle(x => x.Id == seeded.CourseId).Subject;
        echoedCourse.ServingSize.Should().Be(3.0m);
        echoedCourse.UpdatedAt.Should().Be(response.ServerTime);

        // DB must reflect the new value, stamped with serverNow
        var db = await FindCourseAsync(seeded.CourseId, ct);
        db.Should().NotBeNull();
        db!.ServingSize.Should().Be(3.0m);
        db.UpdatedAt.Should().Be(response.ServerTime);
    }

    // Soft Deletes / Tombstones

    [Fact]
    public async Task Sync_PushTombstone_ShouldStoreDeletedAtAndEchoBack()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);
        var courseId = Guid.NewGuid();

        // Push a brand-new course that is already soft-deleted on the client side
        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: courseId,
                    ProductId: globalProductId,
                    ServingSize: 1.0m,
                    TimeOfDay: new TimeOnly(8, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                    DeletedAt: DateTimeOffset.UtcNow.AddMinutes(-2)) // tombstone
            ],
            IntakeLogs: []);

        var response = await PostSyncAsync(request, accessToken, ct);

        // Response must echo the tombstone back to the client
        var echoedCourse = response.Courses.Should().ContainSingle(x => x.Id == courseId).Subject;
        echoedCourse.DeletedAt.Should().NotBeNull();
        echoedCourse.UpdatedAt.Should().Be(response.ServerTime);

        // DB must persist the DeletedAt, and ownership must be injected correctly
        var db = await FindCourseAsync(courseId, ct);
        db.Should().NotBeNull();
        db!.DeletedAt.Should().NotBeNull();
        db.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Sync_PullTombstone_ShouldReturnDeletedItemInDelta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);

        // Seed a soft-deleted course whose UpdatedAt falls after the LastSyncAt we will send
        var tombstoneTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        var courseId = await SeedSoftDeletedCourseAsync(userId, globalProductId, tombstoneTime, ct);

        // LastSyncAt is 1 minute before the tombstone was written:
        //      since = lastSyncAt.AddSeconds(-1) = T-3m1s  <  T-2m = tombstoneTime
        // so the tombstone UpdatedAt is within the delta window
        var lastSyncAt = tombstoneTime.AddMinutes(-1);
        var response = await PostSyncAsync(CreateEmptySyncRequest(lastSyncAt), accessToken, ct);

        var tombstone = response.Courses.Should().ContainSingle(x => x.Id == courseId).Subject;
        tombstone.DeletedAt.Should().NotBeNull();
    }

    // Security

    [Fact]
    public async Task Sync_TakeoverAttempt_UserBModifyingUserACourse_ShouldReturnForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, tokenA) = await CreateAuthenticatedUserAsync(ct);
        var (_, tokenB) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);

        // User A creates a course.
        var courseId = Guid.NewGuid();
        await PostSyncAsync(new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: courseId,
                    ProductId: globalProductId,
                    ServingSize: 1.0m,
                    TimeOfDay: new TimeOnly(9, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                    DeletedAt: null)
            ],
            IntakeLogs: []),
            tokenA, ct);

        // User B attempts to push an update to User A's courseId using their own token.
        // The LWW timestamp is newer to ensure the takeover check is reached, not the stale guard
        var takeoverRequest = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: courseId, // User A's course
                    ProductId: globalProductId,
                    ServingSize: 99.9m,
                    TimeOfDay: new TimeOnly(0, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1), // newer -> passes LWW check, hits ownership guard
                    DeletedAt: null)
            ],
            IntakeLogs: []);

        var response = await PostSyncRawAsync(takeoverRequest, tokenB, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // FluentValidation Rejections

    [Fact]
    public async Task Sync_Validation_ExceedMaxItemsPerEntity_ShouldReturnBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);

        // SyncLimits.MaxItemsPerEntity is the hard cap; sending +1 must be rejected
        var tooManyCourses = Enumerable
            .Range(0, SyncLimits.MaxItemsPerEntity + 1)
            .Select(_ => new SyncCourseDto(
                Id: Guid.NewGuid(),
                ProductId: globalProductId,
                ServingSize: 1.0m,
                TimeOfDay: new TimeOnly(8, 0),
                StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                EndDate: null,
                UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                DeletedAt: null))
            .ToList();

        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses: tooManyCourses,
            IntakeLogs: []);

        var response = await PostSyncRawAsync(request, accessToken, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_Validation_UpdatedAtTooFarInFuture_ShouldReturnBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);

        // UpdatedAt is 10 minutes ahead; MaxClockSkewMinutes is 5, so validator must reject it
        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: Guid.NewGuid(),
                    ProductId: globalProductId,
                    ServingSize: 1.0m,
                    TimeOfDay: new TimeOnly(8, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(10),
                    DeletedAt: null)
            ],
            IntakeLogs: []);

        var response = await PostSyncRawAsync(request, accessToken, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_Validation_DuplicateIdsInBatch_ShouldReturnBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var globalProductId = await GetAnyGlobalProductIdAsync(ct);
        var duplicateId = Guid.NewGuid();

        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses:
            [
                new SyncCourseDto(
                    Id: duplicateId,
                    ProductId: globalProductId,
                    ServingSize: 1.0m,
                    TimeOfDay: new TimeOnly(8, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                    DeletedAt: null),
                new SyncCourseDto(
                    Id: duplicateId, // same ID — must be rejected
                    ProductId: globalProductId,
                    ServingSize: 2.0m,
                    TimeOfDay: new TimeOnly(9, 0),
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    EndDate: null,
                    UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
                    DeletedAt: null)
            ],
            IntakeLogs: []);

        var response = await PostSyncRawAsync(request, accessToken, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_PushFullGraph_ShouldInsertAllEntitiesCorrectlyAndReturnAcks()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, accessToken) = await CreateAuthenticatedUserAsync(ct);
        var globalIngredientId = await GetAnyGlobalIngredientIdAsync(ct);

        var productId = Guid.NewGuid();
        var productIngredientId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var intakeLogId = Guid.NewGuid();

        // Original time has 7 digits (tics)
        var rawTime = DateTimeOffset.UtcNow;
        // Cutting off the 7th digit (10 ticks = 1 microsecond) to ensure 6 digits of precision, which is the max that PostgreSQL supports
        var clientTime = new DateTimeOffset(rawTime.Ticks / 10 * 10, TimeSpan.Zero);
        var clientUpdatedAt = clientTime.AddMinutes(-2);
        var takenAt = clientTime.AddMinutes(-5);

        const string productName = "My Custom Whey";
        const string productDescription = "Bought offline";

        var request = new SyncRequest(
            LastSyncAt: null,
            ClientTime: clientTime,
            Products:
            [
                new SyncProductDto(
                    Id: productId,
                    Name: productName,
                    Description: productDescription,
                    UpdatedAt: clientUpdatedAt,
                    DeletedAt: null)
            ],
            ProductIngredients:
            [
                new SyncProductIngredientDto(
                    Id: productIngredientId,
                    ProductId: productId,
                    IngredientId: globalIngredientId,
                    CustomIngredientName: null,
                    Amount: 30m,
                    Unit: "g",
                    UpdatedAt: clientUpdatedAt,
                    DeletedAt: null)
            ],
            Courses:
            [
                new SyncCourseDto(
                    Id: courseId,
                    ProductId: productId,
                    ServingSize: 1.0m,
                    TimeOfDay: new TimeOnly(10, 0),
                    StartDate: DateOnly.FromDateTime(clientTime.UtcDateTime),
                    EndDate: null,
                    UpdatedAt: clientUpdatedAt,
                    DeletedAt: null)
            ],
            IntakeLogs:
            [
                new SyncIntakeLogDto(
                    Id: intakeLogId,
                    CourseId: courseId,
                    ActualServingSize: 1.0m,
                    TakenAt: takenAt,
                    UpdatedAt: clientUpdatedAt,
                    DeletedAt: null)
            ]);

        var response = await PostSyncAsync(request, accessToken, ct);

        var ackProduct = response.Products.Should().ContainSingle(x => x.Id == productId).Subject;
        ackProduct.Name.Should().Be(productName);
        ackProduct.Description.Should().Be(productDescription);
        ackProduct.DeletedAt.Should().BeNull();
        ackProduct.CreatorUserId.Should().Be(userId);
        ackProduct.UpdatedAt.Should().Be(response.ServerTime);

        var ackPi = response.ProductIngredients.Should().ContainSingle(x => x.Id == productIngredientId).Subject;
        ackPi.ProductId.Should().Be(productId);
        ackPi.IngredientId.Should().Be(globalIngredientId);
        ackPi.Amount.Should().Be(30m);
        ackPi.Unit.Should().Be("g");
        ackPi.UpdatedAt.Should().Be(response.ServerTime);

        var ackCourse = response.Courses.Should().ContainSingle(x => x.Id == courseId).Subject;
        ackCourse.ProductId.Should().Be(productId);
        ackCourse.ServingSize.Should().Be(1.0m);
        ackCourse.TimeOfDay.Should().Be(new TimeOnly(10, 0));
        ackCourse.StartDate.Should().Be(DateOnly.FromDateTime(clientTime.UtcDateTime));
        ackCourse.UpdatedAt.Should().Be(response.ServerTime);

        var ackLog = response.IntakeLogs.Should().ContainSingle(x => x.Id == intakeLogId).Subject;
        ackLog.CourseId.Should().Be(courseId);
        ackLog.ActualServingSize.Should().Be(1.0m);
        ackLog.TakenAt.Should().Be(takenAt);
        ackLog.UpdatedAt.Should().Be(response.ServerTime);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dbProduct = await db.Products.AsNoTracking().SingleAsync(x => x.Id == productId, ct);
        dbProduct.CreatorUserId.Should().Be(userId);
        dbProduct.Name.Should().Be(productName);
        dbProduct.Description.Should().Be(productDescription);
        dbProduct.UpdatedAt.Should().Be(response.ServerTime);

        var dbPi = await db.ProductIngredients.AsNoTracking().SingleAsync(x => x.Id == productIngredientId, ct);
        dbPi.ProductId.Should().Be(productId);
        dbPi.IngredientId.Should().Be(globalIngredientId);
        dbPi.Amount.Should().Be(30m);
        dbPi.UpdatedAt.Should().Be(response.ServerTime);

        var dbCourse = await db.Courses.AsNoTracking().SingleAsync(x => x.Id == courseId, ct);
        dbCourse.UserId.Should().Be(userId);
        dbCourse.ProductId.Should().Be(productId);
        dbCourse.UpdatedAt.Should().Be(response.ServerTime);

        var dbLog = await db.IntakeLogs.AsNoTracking().SingleAsync(x => x.Id == intakeLogId, ct);
        dbLog.UserId.Should().Be(userId);
        dbLog.CourseId.Should().Be(courseId);
        dbLog.UpdatedAt.Should().Be(response.ServerTime);
    }

    private static SyncRequest CreateEmptySyncRequest(DateTimeOffset? lastSyncAt = null)
    {
        return new SyncRequest(
            LastSyncAt: lastSyncAt,
            ClientTime: DateTimeOffset.UtcNow,
            Products: [],
            ProductIngredients: [],
            Courses: [],
            IntakeLogs: []);
    }

    private async Task<(Guid UserId, string AccessToken)> CreateAuthenticatedUserAsync(CancellationToken ct)
    {
        var email = $"sync-{Guid.NewGuid():N}@vitalog.local";
        var response = await Client.PostAsJsonAsync("/api/dev/token", new DevTokenRequest(email), ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<DevTokenResponse>(cancellationToken: ct);
        payload.Should().NotBeNull();
        payload!.UserId.Should().NotBeEmpty();
        payload.AccessToken.Should().NotBeNullOrWhiteSpace();

        return (payload.UserId, payload.AccessToken);
    }

    private async Task<SyncResponse> PostSyncAsync(SyncRequest request, string accessToken, CancellationToken ct)
    {
        var response = await PostSyncRawAsync(request, accessToken, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<SyncResponse>(_jsonOptions, ct);
        payload.Should().NotBeNull();
        return payload!;
    }

    private async Task<HttpResponseMessage> PostSyncRawAsync(SyncRequest request, string? accessToken, CancellationToken ct)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/sync")
        {
            Content = JsonContent.Create(request)
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await Client.SendAsync(message, ct);
    }

    private async Task<SeededUserGraph> SeedUserGraphAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);

        var ingredientId = await db.GlobalIngredients
            .AsNoTracking()
            .Select(static x => x.Id)
            .FirstAsync(ct);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "User Product For Sync",
            Description = "Pre-seeded for first sync test",
            CreatorUserId = userId,
            UpdatedAt = now
        };

        var productIngredient = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            IngredientId = ingredientId,
            Amount = 100m,
            Unit = "mg",
            UpdatedAt = now
        };

        var course = new Course
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = product.Id,
            ServingSize = 1.5m,
            TimeOfDay = new TimeOnly(7, 0),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-2)),
            EndDate = null,
            UpdatedAt = now
        };

        var intakeLog = new IntakeLog
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,
            UserId = userId,
            ActualServingSize = 1.5m,
            TakenAt = now.AddDays(-1),
            UpdatedAt = now
        };

        db.AddRange(product, productIngredient, course, intakeLog);
        await db.SaveChangesAsync(ct);

        return new SeededUserGraph(product.Id, productIngredient.Id, course.Id, intakeLog.Id);
    }

    private async Task<int> CountGlobalIngredientsAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.GlobalIngredients.CountAsync(ct);
    }

    private async Task<Guid> GetAnyGlobalProductIdAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products
            .AsNoTracking()
            .Where(static x => x.CreatorUserId == null)
            .Select(static x => x.Id)
            .FirstAsync(ct);
    }

    private async Task<Guid> GetAnyGlobalIngredientIdAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.GlobalIngredients
            .AsNoTracking()
            .Select(static x => x.Id)
            .FirstAsync(ct);
    }

    private async Task<Course?> FindCourseAsync(Guid courseId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId, ct);
    }

    private async Task<Guid> SeedSoftDeletedCourseAsync(
        Guid userId,
        Guid productId,
        DateTimeOffset tombstoneTime,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var course = new Course
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = productId,
            ServingSize = 1.0m,
            TimeOfDay = new TimeOnly(8, 0),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            EndDate = null,
            UpdatedAt = tombstoneTime,
            DeletedAt = tombstoneTime
        };

        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return course.Id;
    }

    private sealed record DevTokenResponse(string AccessToken, Guid UserId);
    private sealed record SeededUserGraph(Guid ProductId, Guid ProductIngredientId, Guid CourseId, Guid IntakeLogId);
}
