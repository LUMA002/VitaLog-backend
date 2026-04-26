using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Infrastructure.Auth;
using VitaLog.Api.Infrastructure.Database;
using VitaLog.Api.Infrastructure.Validation;

namespace VitaLog.Api.Features.Auth;

public static class LoginEndpoint
{
    public readonly record struct LoginDependencies(
        AppDbContext Db,
        IPasswordHasher<User> PasswordHasher,
        IJwtProvider JwtProvider,
        IRefreshTokenProvider RefreshTokenProvider,
        TimeProvider TimeProvider);

    public static RouteHandlerBuilder MapLogin(this RouteGroupBuilder group)
    {
        return group.MapPost("/login", static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> (
            LoginRequest request,
            [AsParameters] LoginDependencies deps,
            CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await deps.Db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null, ct);

            if (user is null)
            {
                return TypedResults.Problem("Invalid credentials", statusCode: StatusCodes.Status400BadRequest);
            }

            var passwordVerification = deps.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (passwordVerification == PasswordVerificationResult.Failed)
            {
                return TypedResults.Problem("Invalid credentials", statusCode: StatusCodes.Status400BadRequest);
            }

            var (accessToken, _) = deps.JwtProvider.CreateAccessToken(user.Id, user.Email, user.Roles);
            var (plainTextToken, tokenHash, refreshTokenExpiresAtUtc) = deps.RefreshTokenProvider.CreateRefreshToken();

            var now = deps.TimeProvider.GetUtcNow();
            deps.Db.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = tokenHash,
                CreatedAt = now,
                ExpiresAt = refreshTokenExpiresAtUtc,
                UpdatedAt = now
            });

            await deps.Db.SaveChangesAsync(ct);

            return TypedResults.Ok(new LoginResponse(accessToken, plainTextToken));
        })
        .WithName("Login")
        .WithSummary("Sign in using email and password")
        .AddValidationFilter<LoginRequest>()
        .AllowAnonymous();
    }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string AccessToken, string RefreshToken);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(static x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);

        RuleFor(static x => x.Password)
            .NotEmpty();
    }
}
