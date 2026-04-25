using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Domain.Enums;
using VitaLog.Api.Infrastructure.Auth;
using VitaLog.Api.Infrastructure.Database;

namespace VitaLog.Api.Features.Auth;

public static class DevAuthEndpoint
{
    public static void MapDevAuth(this IEndpointRouteBuilder app)
    {
        // this route is only for development purposes,
        // it allows to get a JWT token for any email without password
        app.MapPost("/api/dev/token", async (
            [FromBody] DevTokenRequest request,
            AppDbContext db,
            IJwtProvider jwtProvider,
            CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            if (user is null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    PasswordHash = "DEV_MEGAPASS",
                    Roles = Role.User | Role.Admin,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }

            var (token, _) = jwtProvider.CreateAccessToken(user.Id, user.Email, user.Roles);
            return TypedResults.Ok(new { AccessToken = token, UserId = user.Id });
        })
        .WithTags("Development")
        .WithSummary("Generate instant token for testing without password");
    }
}

public sealed record DevTokenRequest(string Email = "test@vitalog.local");