using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VitaLog.Api.Features.Auth;
using VitaLog.Api.IntegrationTests.Infrastructure;

namespace VitaLog.Api.IntegrationTests.Features.Auth;

public class RegisterEndpointTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Register_WithValidData_ShouldReturnOkAndUserId()
    {
        // Arrange
        var request = new RegisterRequest("test.user@vitalog.com", "StrongPass123");
        var ct = TestContext.Current.CancellationToken; // take the token from xUnit

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/register", request, ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>(ct);
        result.Should().NotBeNull();
        result.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ShouldReturnConflict()
    {
        // Arrange
        var request = new RegisterRequest("duplicate@vitalog.com", "StrongPass123");
        var ct = TestContext.Current.CancellationToken;

        // First, register successfully
        await Client.PostAsJsonAsync("/api/auth/register", request, ct);

        // Act (duplicate request)
        var response = await Client.PostAsJsonAsync("/api/auth/register", request, ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WithInvalidPasswordComplexity_ShouldReturnBadRequest()
    {
        // Arrange (Password shorter than 8 characters and without uppercase letters)
        var request = new RegisterRequest("test@vitalog.com", "weak");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/register", request, ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithUppercaseEmailAndExistingLowercaseEmail_ShouldReturnConflict()
    {
        var ct = TestContext.Current.CancellationToken;

        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("case.test@vitalog.com", "StrongPass123"),
            ct);

        var response = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("CASE.TEST@VITALOG.COM", "StrongPass123"),
            ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}