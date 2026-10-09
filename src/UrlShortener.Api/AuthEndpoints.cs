using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api;

public static class AuthEndpoints
{
    public const string SessionScheme = "LinkForge.Session";
    public const string ExternalScheme = "LinkForge.External";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/auth").WithTags("Auth");

        api.MapGet("/providers", (IConfiguration configuration) => Results.Ok(new
        {
            google = GoogleEnabled(configuration)
        }));

        api.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        api.MapPost("/signup", async (SignUpRequest request, HttpContext context,
            IAntiforgery antiforgery, UrlShortenerDbContext database,
            IPasswordHasher<User> passwordHasher, CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return BadCsrf();

            var name = request.Name?.Trim();
            var email = NormalizeEmail(request.Email);
            if (name is null || name.Length is < 2 or > 200 || email is null ||
                request.Password is null || request.Password.Length is < 12 or > 128)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["account"] = ["Enter a name (2–200 characters), valid email, and password (12–128 characters)."]
                });
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = name,
                Email = email,
                CreatedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            database.Users.Add(user);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException error) when (IsUniqueViolation(error))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Email already registered.");
            }

            await SignInAsync(context, user);
            return Results.Created("/api/v1/auth/me", ToResponse(user));
        }).RequireRateLimiting("account-write");

        api.MapPost("/login", async (LoginRequest request, HttpContext context,
            IAntiforgery antiforgery, UrlShortenerDbContext database,
            IPasswordHasher<User> passwordHasher, CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return BadCsrf();

            var email = NormalizeEmail(request.Email);
            if (email is null || string.IsNullOrEmpty(request.Password))
                return InvalidCredentials();

            var user = await database.Users.SingleOrDefaultAsync(item => item.Email == email, cancellationToken);
            if (user?.PasswordHash is null ||
                passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
                == PasswordVerificationResult.Failed)
            {
                return InvalidCredentials();
            }

            await SignInAsync(context, user);
            return Results.Ok(ToResponse(user));
        }).RequireRateLimiting("account-write");

        api.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return BadCsrf();
            await context.SignOutAsync(SessionScheme);
            return Results.NoContent();
        });

        api.MapGet("/me", async (HttpContext context, UrlShortenerDbContext database,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                return Results.Unauthorized();

            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(ToResponse(user));
        });

        api.MapGet("/google", (IConfiguration configuration) =>
        {
            if (!GoogleEnabled(configuration))
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Google sign-in is not configured.");

            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = "/api/v1/auth/google/complete"
            }, [GoogleDefaults.AuthenticationScheme]);
        }).RequireRateLimiting("account-write");

        api.MapGet("/google/complete", async (HttpContext context,
            UrlShortenerDbContext database, CancellationToken cancellationToken) =>
        {
            var external = await context.AuthenticateAsync(ExternalScheme);
            var principal = external.Principal;
            var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = NormalizeEmail(principal?.FindFirstValue(ClaimTypes.Email));
            var verified = principal?.FindFirstValue("google_verified_email");
            await context.SignOutAsync(ExternalScheme);

            if (!external.Succeeded || string.IsNullOrWhiteSpace(subject) || email is null ||
                !string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase))
                return Results.Redirect("/app/login.html?error=google-failed");

            var user = await database.Users.SingleOrDefaultAsync(item => item.GoogleSubject == subject, cancellationToken);
            if (user is not null && !string.Equals(user.Email, email, StringComparison.Ordinal))
                return Results.Redirect("/app/login.html?error=google-failed");
            if (user is null)
            {
                if (await database.Users.AnyAsync(item => item.Email == email, cancellationToken))
                    return Results.Redirect("/app/login.html?error=account-exists");

                var name = principal!.FindFirstValue(ClaimTypes.Name)?.Trim();
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name[..Math.Min(name.Length, 200)],
                    Email = email,
                    GoogleSubject = subject,
                    AvatarUrl = GoogleAvatar(principal!.FindFirstValue("google_picture")),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                database.Users.Add(user);
                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException error) when (IsUniqueViolation(error))
                {
                    return Results.Redirect("/app/login.html?error=account-exists");
                }
            }

            await SignInAsync(context, user);
            return Results.Redirect("/app/");
        });
    }

    private static bool GoogleEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Auth:Google:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["Auth:Google:ClientSecret"]);

    private static string? NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant();
        return email is { Length: > 0 and <= 320 } &&
               !email.Any(char.IsWhiteSpace) &&
               MailAddress.TryCreate(email, out var parsed) &&
               parsed.Address == email ? email : null;
    }

    private static bool IsUniqueViolation(DbUpdateException error) =>
        error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult BadCsrf() => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
        title: "A valid CSRF token is required.");

    private static IResult InvalidCredentials() => Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
        title: "Invalid email or password.");

    private static string? GoogleAvatar(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)) &&
        value!.Length <= 2048 ? value : null;

    private static AuthUserResponse ToResponse(User user) => new(user.Id, user.Name, user.Email, user.AvatarUrl);

    private static Task SignInAsync(HttpContext context, User user)
    {
        context.Response.Headers.CacheControl = "no-store";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email)
        };
        return context.SignInAsync(SessionScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, SessionScheme)));
    }
}

public sealed record SignUpRequest(string? Name, string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record AuthUserResponse(Guid Id, string Name, string Email, string? AvatarUrl);
