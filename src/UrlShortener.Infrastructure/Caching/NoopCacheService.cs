using UrlShortener.Application;

namespace UrlShortener.Infrastructure.Caching;

public sealed class NoopCacheService : ICacheService
{
    public Task<CacheLookup> GetAsync(string shortCode, CancellationToken cancellationToken) =>
        Task.FromResult(new CacheLookup(CacheState.Miss));

    public Task SetIfAbsentAsync(string shortCode, string originalUrl, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task MarkDeletedAsync(string shortCode, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
