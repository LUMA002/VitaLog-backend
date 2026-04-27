using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VitaLog.Api.Features.Auth;
using VitaLog.Api.IntegrationTests.Infrastructure;

namespace VitaLog.Api.IntegrationTests.Features.Auth;

public class LoginEndpointTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Login_AsMobileClient_ShouldReturnTokensInJson()
    {
        var ct = TestContext.Current.CancellationToken;

        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("login_test@vitalog.com", "StrongPass123!"),
            ct);

        var request = new LoginRequest("login_test@vitalog.com", "StrongPass123!");

        var response = await Client.PostAsJsonAsync("/api/auth/login", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_AsWebClient_ShouldReturnCookiesAndEmptyJson()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("web_test@vitalog.com", "StrongPass123!"),
            ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("web_test@vitalog.com", "StrongPass123!"))
        };
        request.Headers.Add("X-Client-Platform", "web");
        
        // Act
        var response = await Client.SendAsync(request, ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        result.Should().NotBeNull();
        result.AccessToken.Should().BeEmpty();
        result.RefreshToken.Should().BeEmpty();

        response.Headers.TryGetValues("Set-Cookie", out var setCookieValues).Should().BeTrue();
        setCookieValues.Should().NotBeNull();
        setCookieValues.Should().Contain(x => x.Contains("X-Access-Token=", StringComparison.Ordinal));
        setCookieValues.Should().Contain(x => x.Contains("X-Refresh-Token=", StringComparison.Ordinal));
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
