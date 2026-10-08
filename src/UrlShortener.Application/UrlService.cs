using System.Security.Cryptography;
using UrlShortener.Domain;

namespace UrlShortener.Application;

public sealed record CreatedUrl(string ShortCode, string OriginalUrl, string ManagementToken, DateTimeOffset CreatedAt);
public sealed record UrlInfo(string ShortCode, string OriginalUrl, DateTimeOffset CreatedAt);

public enum DeleteResult
{
    Deleted,
    NotFound,
    Forbidden
}

public sealed class UrlService(IUrlRepository repository, ICacheService cache, TimeProvider timeProvider)
{
    public async Task<CreatedUrl> CreateAsync(string originalUrl, CancellationToken cancellationToken)
    {
        if (!UrlValidator.IsValid(originalUrl))
        {
            throw new ArgumentException("A valid HTTP(S) URL is required.", nameof(originalUrl));
        }

        var id = await repository.NextIdAsync(cancellationToken);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var entry = new UrlEntry
        {
            Id = id,
            ShortCode = Base62.EncodeSeven(id),
            OriginalUrl = originalUrl,
            ManagementTokenHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)),
            CreatedAt = timeProvider.GetUtcNow()
        };

        await repository.AddAsync(entry, cancellationToken);
        return new CreatedUrl(entry.ShortCode, entry.OriginalUrl, token, entry.CreatedAt);
    }

    public async Task<UrlInfo?> FindAsync(string shortCode, CancellationToken cancellationToken)
    {
        var entry = await repository.FindAsync(shortCode, cancellationToken);
        return entry is null ? null : new UrlInfo(entry.ShortCode, entry.OriginalUrl, entry.CreatedAt);
    }

    public async Task<string?> FindRedirectTargetAsync(string shortCode, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(shortCode, cancellationToken);
        if (cached.State == CacheState.Found)
        {
            return cached.OriginalUrl;
        }

        if (cached.State == CacheState.Deleted)
        {
            return null;
        }

        var entry = await repository.FindAsync(shortCode, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        await cache.SetIfAbsentAsync(shortCode, entry.OriginalUrl, cancellationToken);
        if ((await cache.GetAsync(shortCode, cancellationToken)).State == CacheState.Deleted)
        {
            return null;
        }

        return entry.OriginalUrl;
    }

    public async Task<DeleteResult> DeleteAsync(string shortCode, string? token, CancellationToken cancellationToken)
    {
        var entry = await repository.FindAsync(shortCode, cancellationToken);
        if (entry is null)
        {
            return DeleteResult.NotFound;
        }

        if (token is null || token.Length != 43 ||
            token.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            return DeleteResult.Forbidden;
        }

        var suppliedHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, entry.ManagementTokenHash))
        {
            return DeleteResult.Forbidden;
        }

        await repository.DeleteAsync(entry, cancellationToken);
        await cache.MarkDeletedAsync(shortCode, cancellationToken);
        return DeleteResult.Deleted;
    }
}
