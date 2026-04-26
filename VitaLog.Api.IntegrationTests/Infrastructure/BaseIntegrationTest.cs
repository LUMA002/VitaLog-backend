namespace VitaLog.Api.IntegrationTests.Infrastructure;

public abstract class BaseIntegrationTest(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    protected readonly HttpClient Client = factory.CreateClient();
}
