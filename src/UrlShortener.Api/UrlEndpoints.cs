using UrlShortener.Application;
using UrlShortener.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api;

public static class UrlEndpoints
{
    public static void MapUrlEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/urls").WithTags("URLs");

        api.MapPost("/", async (CreateShortUrlRequest request, HttpContext context,
            UrlService service, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!UrlValidator.IsValid(request.Url))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["url"] = ["Provide an absolute HTTP(S) URL with a DNS host, no credentials, and at most 2048 characters."]
                });
            }

            Guid? userId = Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId)
                ? parsedId : null;
            var created = await service.CreateAsync(request.Url!, cancellationToken, userId);
            var baseUrl = configuration["ShortUrls:BaseUrl"]?.TrimEnd('/')
                ?? throw new InvalidOperationException("ShortUrls:BaseUrl is required.");
            var response = new CreateShortUrlResponse(
                created.ShortCode,
                $"{baseUrl}/{created.ShortCode}",
                created.ManagementToken,
                created.CreatedAt);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Created($"/api/v1/urls/{created.ShortCode}", response);
        })
        .WithName("CreateShortUrl")
        .Produces<CreateShortUrlResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        api.MapGet("/mine", async (int? page, int? pageSize, HttpContext context,
            UrlShortenerDbContext database, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (page is < 1 or > 42_000_000 || pageSize is < 1 or > 50)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["page"] = ["Page must be positive and pageSize must be between 1 and 50."]
                });

            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
                return Results.Unauthorized();
            var currentPage = page ?? 1;
            var size = pageSize ?? 20;
            var query = database.Urls.AsNoTracking().Where(url => url.UserId == ownerId);
            var total = await query.CountAsync(cancellationToken);
            var baseUrl = configuration["ShortUrls:BaseUrl"]?.TrimEnd('/')
                ?? throw new InvalidOperationException("ShortUrls:BaseUrl is required.");
            var links = await query.OrderByDescending(url => url.CreatedAt).ThenByDescending(url => url.Id)
                .Skip((currentPage - 1) * size).Take(size)
                .Select(url => new MyLinkResponse(url.ShortCode, $"{baseUrl}/{url.ShortCode}",
                    url.OriginalUrl, url.ClickCount, url.CreatedAt))
                .ToListAsync(cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new MyLinksResponse(links, currentPage, size, total));
        }).RequireAuthorization().WithName("ListMyLinks");

        api.MapPatch("/{shortCode}", async (string shortCode, ChangeShortCodeRequest request,
            HttpContext context, IAntiforgery antiforgery, UrlShortenerDbContext database,
            ICacheService cache, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
                return Results.Problem(statusCode: 400, title: "A valid CSRF token is required.");
            if (!IsSupportedCode(shortCode)) return NotFound();
            var newCode = request.ShortCode?.Trim();
            if (newCode == shortCode)
            {
                var sameOwnerId = Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedOwner)
                    ? parsedOwner : Guid.Empty;
                var unchanged = await database.Urls.AsNoTracking()
                    .SingleOrDefaultAsync(url => url.ShortCode == shortCode && url.UserId == sameOwnerId, cancellationToken);
                if (unchanged is null) return NotFound();
                var unchangedBaseUrl = configuration["ShortUrls:BaseUrl"]?.TrimEnd('/')
                    ?? throw new InvalidOperationException("ShortUrls:BaseUrl is required.");
                return Results.Ok(new MyLinkResponse(shortCode, $"{unchangedBaseUrl}/{shortCode}",
                    unchanged.OriginalUrl, unchanged.ClickCount, unchanged.CreatedAt));
            }
            if (!IsSupportedCode(newCode) || ReservedRoutes.Contains(newCode!) ||
                (newCode!.Length == 7 && Base62.TryDecode(newCode, out _)))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["shortCode"] = ["Use 3–16 ASCII letters, digits, hyphens or underscores; start and end with a letter or digit. Seven-character alphanumeric codes and application routes are reserved."]
                });

            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
                return Results.Unauthorized();
            var entry = await database.Urls.AsNoTracking()
                .SingleOrDefaultAsync(url => url.ShortCode == shortCode && url.UserId == ownerId, cancellationToken);
            if (entry is null) return NotFound();
            var baseUrl = configuration["ShortUrls:BaseUrl"]?.TrimEnd('/')
                ?? throw new InvalidOperationException("ShortUrls:BaseUrl is required.");
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            database.ReservedShortCodes.Add(new ReservedShortCode { Code = newCode! });
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Problem(statusCode: 409, title: "Short code is already in use.");
            }

            var changed = await database.Urls
                .Where(url => url.Id == entry.Id && url.ShortCode == shortCode && url.UserId == ownerId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(url => url.ShortCode, newCode!), cancellationToken);
            if (changed == 0) return Results.Problem(statusCode: 409, title: "Link changed. Refresh and try again.");
            await transaction.CommitAsync(cancellationToken);
            await cache.MarkDeletedAsync(shortCode, cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new MyLinkResponse(newCode!, $"{baseUrl}/{newCode}",
                entry.OriginalUrl, entry.ClickCount, entry.CreatedAt));
        }).RequireAuthorization().WithName("ChangeShortCode");

        api.MapGet("/{shortCode}", async (string shortCode, UrlService service,
            CancellationToken cancellationToken) =>
        {
            if (!IsSupportedCode(shortCode))
            {
                return NotFound();
            }

            var url = await service.FindAsync(shortCode, cancellationToken);
            return url is null ? NotFound() : Results.Ok(url);
        })
        .WithName("GetShortUrl")
        .Produces<UrlInfo>()
        .Produces(StatusCodes.Status404NotFound);

        api.MapDelete("/{shortCode}", async (string shortCode, HttpContext context,
            UrlService service, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        {
            if (!IsSupportedCode(shortCode))
            {
                return NotFound();
            }

            var token = context.Request.Headers["X-Management-Token"].ToString();
            if (string.IsNullOrEmpty(token) &&
                Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
            {
                if (!await antiforgery.IsRequestValidAsync(context))
                    return Results.Problem(statusCode: 400, title: "A valid CSRF token is required.");
                var ownedOutcome = await service.DeleteOwnedAsync(shortCode, ownerId, cancellationToken);
                return ownedOutcome == DeleteResult.Deleted ? Results.NoContent() : NotFound();
            }
            var outcome = await service.DeleteAsync(shortCode, token, cancellationToken);
            return outcome switch
            {
                DeleteResult.Deleted => Results.NoContent(),
                DeleteResult.NotFound => NotFound(),
                _ => Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "A valid management token is required.")
            };
        })
        .WithName("DeleteShortUrl")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/{shortCode}", async (string shortCode, UrlService service,
            UrlShortenerDbContext database, ILogger<UrlService> logger,
            CancellationToken cancellationToken) =>
        {
            if (!IsSupportedCode(shortCode))
            {
                return NotFound();
            }

            var originalUrl = await service.FindRedirectTargetAsync(shortCode, cancellationToken);
            if (originalUrl is null) return NotFound();
            try
            {
                await database.Urls.Where(url => url.ShortCode == shortCode)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(url => url.ClickCount,
                        url => url.ClickCount + 1), cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Click accounting failed for short code {ShortCode}", shortCode);
            }
            return Results.Redirect(originalUrl, permanent: false);
        })
        .WithName("RedirectShortUrl")
        .WithTags("Redirect")
        .Produces(StatusCodes.Status302Found)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static readonly HashSet<string> ReservedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "api", "health", "swagger", "openapi", "signin-google"
    };

    private static bool IsSupportedCode(string? code) => code is { Length: >= 3 and <= 16 } &&
        char.IsAsciiLetterOrDigit(code[0]) && char.IsAsciiLetterOrDigit(code[^1]) &&
        code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static IResult NotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Short URL not found.");
}

public sealed record CreateShortUrlRequest(string? Url);
public sealed record CreateShortUrlResponse(
    string ShortCode,
    string ShortUrl,
    string ManagementToken,
    DateTimeOffset CreatedAt);

public sealed record ChangeShortCodeRequest(string? ShortCode);
public sealed record MyLinkResponse(string ShortCode, string ShortUrl, string OriginalUrl,
    long ClickCount, DateTimeOffset CreatedAt);
public sealed record MyLinksResponse(IReadOnlyList<MyLinkResponse> Links, int Page, int PageSize, int Total);
