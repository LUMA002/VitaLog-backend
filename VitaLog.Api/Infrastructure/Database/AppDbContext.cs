using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Entities;

namespace VitaLog.Api.Infrastructure.Database;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<GlobalIngredient> GlobalIngredients => Set<GlobalIngredient>();
    public DbSet<ProductIngredient> ProductIngredients => Set<ProductIngredient>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<IntakeLog> IntakeLogs => Set<IntakeLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}