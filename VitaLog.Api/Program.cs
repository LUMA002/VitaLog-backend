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
using VitaLog.Api.Features.Sync;
using VitaLog.Api.Infrastructure.Auth;
using VitaLog.Api.Infrastructure.Database;
using VitaLog.Api.Infrastructure.Middleware;
using VitaLog.Api.Infrastructure.Time;

var builder = WebApplication.CreateBuilder(args);

// TODO: consider using Brotli\Gzip tools

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

// Truncates to µs precision — matches PostgreSQL timestamptz and Dart DateTime.
// Must be registered before AddJwtAuth so every service receives the same singleton.
builder.Services.AddSingleton<TimeProvider>(new MicrosecondPrecisionTimeProvider(TimeProvider.System));

builder.Services.AddValidatorsFromAssemblyContaining<Program>(
    lifetime: ServiceLifetime.Singleton,
    includeInternalTypes: true);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<SyncHandler>();

var app = builder.Build();

// Seed the database on startup if the --seed argument is provided, then exit without starting the web server
if (args.Contains("--seed"))
{
    app.Logger.LogInformation("Seeding database...");
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }
    app.Logger.LogInformation("Seeding completed successfully. Exiting application.");
    return; // end the application immediately after seeding without starting the web server
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
// app.UseResponseCompression();

// Mobile dev clients call http://127.0.0.1:5247 via adb reverse. HTTPS redirect
// would send them to :7059 (not reversed) and break with NetworkFailure on device.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
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
apiGroup.MapSyncEndpoint();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("Health")
    .AllowAnonymous();

await app.RunAsync();