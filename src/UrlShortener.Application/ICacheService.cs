namespace UrlShortener.Application;

public enum CacheState
{
    Miss,
    Found,
    Deleted
}

public readonly record struct CacheLookup(CacheState State, string? OriginalUrl = null);

public interface ICacheService
{
    Task<CacheLookup> GetAsync(string shortCode, CancellationToken cancellationToken);
    Task SetIfAbsentAsync(string shortCode, string originalUrl, CancellationToken cancellationToken);
    Task MarkDeletedAsync(string shortCode, CancellationToken cancellationToken);
}
