using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VitaLog.Api.Features.Auth;
using VitaLog.Api.IntegrationTests.Infrastructure;

namespace VitaLog.Api.IntegrationTests.Features.Auth;

public class LoginEndpointTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnTokens()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("login_test@vitalog.com", "StrongPass123!"),
            ct);

        var request = new LoginRequest("login_test@vitalog.com", "StrongPass123!");

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/login", request, ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ShouldReturnBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;

        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("login_wrong_pass@vitalog.com", "StrongPass123!"),
            ct);

        var request = new LoginRequest("login_wrong_pass@vitalog.com", "WrongPass123");

        var response = await Client.PostAsJsonAsync("/api/auth/login", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithNonExistentEmail_ShouldReturnBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new LoginRequest("ghost@vitalog.com", "StrongPass123!");

        var response = await Client.PostAsJsonAsync("/api/auth/login", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
