using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using VitaLog.Api.Infrastructure.Database;

namespace VitaLog.Api.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("vitalog_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Do a migration after the container is up and running
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    // Added override and call to base.DisposeAsync() to ensure proper cleanup of resources
    // and to prevent potential memory leaks or dangling resources after tests are completed.
    public override async ValueTask DisposeAsync()
    {
        await _dbContainer.StopAsync();
        await _dbContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set the environment to Development to ensure that our DevAuthEndpoint is available
        // and that we get detailed error pages if something goes wrong during testing.
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Delete current registration of AppDbContext, which is configured to use the real database
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            // Register a new one that points to our isolated Docker container
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(_dbContainer.GetConnectionString(), npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                });
            });
        });
    }
}