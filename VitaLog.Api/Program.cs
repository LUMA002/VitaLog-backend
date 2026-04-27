using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using System.Globalization;
using System.Text.Json.Serialization;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Features.Auth;
using VitaLog.Api.Features.Directory;
using VitaLog.Api.Features.Products;
using VitaLog.Api.Infrastructure.Auth;
using VitaLog.Api.Infrastructure.Database;
using VitaLog.Api.Infrastructure.Middleware;

var builder = WebApplication.CreateBuilder(args);

ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("en-US");

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Configure it via User Secrets.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
    });

    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors();
        options.EnableSensitiveDataLogging();
    }
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Serialize enums as strings in responses for better readability
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>(
    lifetime: ServiceLifetime.Singleton,
    includeInternalTypes: true);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddScoped<DatabaseSeeder>();

var app = builder.Build();

// Seed the database on startup
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    app.MapDevAuth();
}

app.MapAuthFeature();

var apiGroup = app.MapGroup("/api");
apiGroup.MapGetIngredients();
apiGroup.MapGetGlobalProducts();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("Health")
    .AllowAnonymous();

await app.RunAsync();