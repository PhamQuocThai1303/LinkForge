using UrlShortener.Application;
using UrlShortener.Domain;
using System.Security.Claims;

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

            long? userId = long.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId)
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

        api.MapGet("/{shortCode}", async (string shortCode, UrlService service,
            CancellationToken cancellationToken) =>
        {
            if (!IsGeneratedCode(shortCode))
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
            UrlService service, CancellationToken cancellationToken) =>
        {
            if (!IsGeneratedCode(shortCode))
            {
                return NotFound();
            }

            var token = context.Request.Headers["X-Management-Token"].ToString();
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
            CancellationToken cancellationToken) =>
        {
            if (!IsGeneratedCode(shortCode))
            {
                return NotFound();
            }

            var originalUrl = await service.FindRedirectTargetAsync(shortCode, cancellationToken);
            return originalUrl is null ? NotFound() : Results.Redirect(originalUrl, permanent: false);
        })
        .WithName("RedirectShortUrl")
        .WithTags("Redirect")
        .Produces(StatusCodes.Status302Found)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static bool IsGeneratedCode(string code) =>
        code.Length == 7 && Base62.TryDecode(code, out _);

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
