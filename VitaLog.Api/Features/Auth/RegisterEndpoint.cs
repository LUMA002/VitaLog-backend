using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VitaLog.Api.Domain.Entities;
using VitaLog.Api.Domain.Enums;
using VitaLog.Api.Infrastructure.Database;
using VitaLog.Api.Infrastructure.Validation;

namespace VitaLog.Api.Features.Auth;

public static class RegisterEndpoint
{
    public static RouteHandlerBuilder MapRegister(this RouteGroupBuilder group)
    {
        return group.MapPost("/register", static async Task<Results<Ok<RegisterResponse>, Conflict<ProblemDetails>>> (
            RegisterRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var exists = await db.Users
                .AnyAsync(u => u.Email == email && u.DeletedAt == null, ct);

            if (exists)
            {
                return TypedResults.Conflict(new ProblemDetails
                {
                    Title = "Email already exists",
                    Status = StatusCodes.Status409Conflict
                });
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                Roles = Role.User,
                UpdatedAt = timeProvider.GetUtcNow()
            };

            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(new RegisterResponse(user.Id));
        })
        .WithName("Register")
        .WithSummary("Register a new account")
        .AddValidationFilter<RegisterRequest>()
        .AllowAnonymous();
    }
}

public sealed record RegisterRequest(string Email, string Password);
public sealed record RegisterResponse(Guid UserId);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(static x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);

        RuleFor(static x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .MaximumLength(128).WithMessage("Password must be at most 128 characters long.")
            .Matches(@"[A-Z]+").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]+").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"[0-9]+").WithMessage("Password must contain at least one number.");
    }
}
