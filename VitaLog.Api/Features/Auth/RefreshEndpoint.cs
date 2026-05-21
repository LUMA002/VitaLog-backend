using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Infrastructure.Auth;
using VitaLog.Api.Infrastructure.Database;
using VitaLog.Api.Infrastructure.Validation;

namespace VitaLog.Api.Features.Auth;

public static class RefreshEndpoint
{
    public readonly record struct RefreshDependencies(
        AppDbContext Db,
        IJwtProvider JwtProvider,
        IRefreshTokenProvider RefreshTokenProvider,
        TimeProvider TimeProvider);

    public static RouteHandlerBuilder MapRefresh(this RouteGroupBuilder group)
    {
        return group.MapPost("/refresh", static async Task<Results<Ok<RefreshResponse>, ProblemHttpResult>> (
            RefreshRequest request,
            HttpContext context,
            [AsParameters] RefreshDependencies deps,
            CancellationToken ct) =>
        {
            var platform = context.Request.Headers["X-Client-Platform"].FirstOrDefault()?.ToLowerInvariant();

            // Client sends plain text token, we hash it to compare with the stored hash in the database
            var providedTokenHash = deps.RefreshTokenProvider.Hash(request.RefreshToken);

            var userToken = await deps.Db.UserRefreshTokens
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Token == providedTokenHash && x.User.DeletedAt == null, ct);

            if (userToken is null || userToken.RevokedAt is not null || userToken.ExpiresAt < deps.TimeProvider.GetUtcNow())
            {
                return TypedResults.Problem("Invalid or expired refresh token", statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = userToken.User;
            var now = deps.TimeProvider.GetUtcNow();

            // Mark the old token as revoked with a timestamp (simplified version of Refresh Token Rotation)
            // TODO: look for "Reuse Detection" - revoke all tokens if reuse is detected and etc.
            userToken.RevokedAt = now;
            userToken.UpdatedAt = now;

            var (accessToken, _) = deps.JwtProvider.CreateAccessToken(user.Id, user.Email, user.Roles);
            var (plainTextToken, tokenHash, refreshTokenExpiresAtUtc) = deps.RefreshTokenProvider.CreateRefreshToken();

            var newUserToken = new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = tokenHash, // hash the token for DB
                CreatedAt = now,
                ExpiresAt = refreshTokenExpiresAtUtc,
                UpdatedAt = now
            };

            deps.Db.UserRefreshTokens.Add(newUserToken);

            await deps.Db.SaveChangesAsync(ct);

            // Handle Web platform (Cookies), same as in LoginEndpoint
            if (platform == "web")
            {
                context.Response.Cookies.Append("X-Access-Token", accessToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = refreshTokenExpiresAtUtc
                });

                context.Response.Cookies.Append("X-Refresh-Token", plainTextToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = refreshTokenExpiresAtUtc
                });

                return TypedResults.Ok(new RefreshResponse(string.Empty, string.Empty));
            }

            // PlainText token for mobile
            return TypedResults.Ok(new RefreshResponse(accessToken, plainTextToken));
        })
        .WithName("Refresh")
        .WithSummary("Refresh access token using a valid refresh token")
        .AddValidationFilter<RefreshRequest>()
        .AllowAnonymous();
    }
}

public sealed record RefreshRequest(string RefreshToken);
public sealed record RefreshResponse(string AccessToken, string RefreshToken);

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(static x => x.RefreshToken)
            .NotEmpty();
    }
}