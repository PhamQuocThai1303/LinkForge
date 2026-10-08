using UrlShortener.Application;
using UrlShortener.Domain;

namespace UrlShortener.UnitTests;

public sealed class UrlServiceCacheTests
{
    [Fact]
    public async Task Cache_hit_redirects_without_a_repository_read()
    {
        var repository = new FakeRepository();
        var cache = new FakeCache();
        await cache.SetIfAbsentAsync("abc1234", "https://example.com/cached", default);
        var service = new UrlService(repository, cache, TimeProvider.System);

        var target = await service.FindRedirectTargetAsync("abc1234", default);

        Assert.Equal("https://example.com/cached", target);
        Assert.Equal(0, repository.FindCount);
    }

    [Fact]
    public async Task Cache_miss_reads_repository_and_populates_cache()
    {
        var repository = new FakeRepository();
        var cache = new FakeCache();
        var service = new UrlService(repository, cache, TimeProvider.System);
        var created = await service.CreateAsync("https://example.com/original", default);

        Assert.Equal(created.OriginalUrl, await service.FindRedirectTargetAsync(created.ShortCode, default));
        Assert.Equal(1, repository.FindCount);
        Assert.Equal(CacheState.Found, (await cache.GetAsync(created.ShortCode, default)).State);
        Assert.Equal(created.OriginalUrl, await service.FindRedirectTargetAsync(created.ShortCode, default));
        Assert.Equal(1, repository.FindCount);
    }

    [Fact]
    public async Task Cache_failure_falls_back_to_repository()
    {
        var repository = new FakeRepository();
        var cache = new FakeCache { FailReads = true };
        var service = new UrlService(repository, cache, TimeProvider.System);
        var created = await service.CreateAsync("https://example.com/original", default);

        Assert.Equal(created.OriginalUrl, await service.FindRedirectTargetAsync(created.ShortCode, default));
        Assert.Equal(1, repository.FindCount);
    }

    [Fact]
    public async Task Delete_tombstone_blocks_a_late_cache_fill()
    {
        var repository = new FakeRepository();
        var cache = new FakeCache();
        var service = new UrlService(repository, cache, TimeProvider.System);
        var created = await service.CreateAsync("https://example.com/original", default);

        Assert.Equal(DeleteResult.Deleted,
            await service.DeleteAsync(created.ShortCode, created.ManagementToken, default));
        await cache.SetIfAbsentAsync(created.ShortCode, created.OriginalUrl, default);

        Assert.Equal(CacheState.Deleted, (await cache.GetAsync(created.ShortCode, default)).State);
        Assert.Null(await service.FindRedirectTargetAsync(created.ShortCode, default));
    }

    [Fact]
    public async Task Redirect_does_not_return_a_stale_database_read_after_concurrent_delete()
    {
        var repository = new FakeRepository();
        var cache = new FakeCache();
        var service = new UrlService(repository, cache, TimeProvider.System);
        var created = await service.CreateAsync("https://example.com/original", default);
        cache.DeleteOnNextSet = true;

        Assert.Null(await service.FindRedirectTargetAsync(created.ShortCode, default));
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, CacheLookup> entries = [];
        public bool FailReads { get; init; }
        public bool DeleteOnNextSet { get; set; }

        public Task<CacheLookup> GetAsync(string shortCode, CancellationToken cancellationToken) =>
            Task.FromResult(FailReads ? new CacheLookup(CacheState.Miss)
                : entries.GetValueOrDefault(shortCode, new CacheLookup(CacheState.Miss)));

        public Task SetIfAbsentAsync(string shortCode, string originalUrl, CancellationToken cancellationToken)
        {
            if (DeleteOnNextSet)
            {
                entries[shortCode] = new CacheLookup(CacheState.Deleted);
                DeleteOnNextSet = false;
                return Task.CompletedTask;
            }

            entries.TryAdd(shortCode, new CacheLookup(CacheState.Found, originalUrl));
            return Task.CompletedTask;
        }

        public Task MarkDeletedAsync(string shortCode, CancellationToken cancellationToken)
        {
            entries[shortCode] = new CacheLookup(CacheState.Deleted);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : IUrlRepository
    {
        private readonly Dictionary<string, UrlEntry> entries = [];
        private long nextId = 100_000_000_000;
        public int FindCount { get; private set; }

        public Task<long> NextIdAsync(CancellationToken cancellationToken) => Task.FromResult(nextId++);

        public Task AddAsync(UrlEntry entry, CancellationToken cancellationToken)
        {
            entries.Add(entry.ShortCode, entry);
            return Task.CompletedTask;
        }

        public Task<UrlEntry?> FindAsync(string shortCode, CancellationToken cancellationToken)
        {
            FindCount++;
            return Task.FromResult(entries.GetValueOrDefault(shortCode));
        }

        public Task DeleteAsync(UrlEntry entry, CancellationToken cancellationToken)
        {
            entries.Remove(entry.ShortCode);
            return Task.CompletedTask;
        }
    }
}
