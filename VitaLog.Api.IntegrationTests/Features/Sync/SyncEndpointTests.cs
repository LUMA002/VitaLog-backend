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

    private async Task<Course?> FindCourseAsync(Guid courseId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId, ct);
    }

    private sealed record DevTokenResponse(string AccessToken, Guid UserId);
    private sealed record SeededUserGraph(Guid ProductId, Guid ProductIngredientId, Guid CourseId, Guid IntakeLogId);
}
