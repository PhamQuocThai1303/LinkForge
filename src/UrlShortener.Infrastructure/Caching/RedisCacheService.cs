using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UrlShortener.Application;

namespace UrlShortener.Infrastructure.Caching;

public sealed class RedisCacheService(IConnectionMultiplexer connection, ILogger<RedisCacheService> logger)
    : ICacheService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private const string DeletedMarker = "deleted";

    public async Task<CacheLookup> GetAsync(string shortCode, CancellationToken cancellationToken)
    {
        if (!connection.IsConnected)
        {
            return new CacheLookup(CacheState.Miss);
        }

        try
        {
            var value = await connection.GetDatabase().StringGetAsync(Key(shortCode)).WaitAsync(cancellationToken);
            if (value.IsNull)
            {
                return new CacheLookup(CacheState.Miss);
            }

            return value == DeletedMarker
                ? new CacheLookup(CacheState.Deleted)
                : new CacheLookup(CacheState.Found, value.ToString());
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Redis read failed for short code {ShortCode}; using PostgreSQL", shortCode);
            return new CacheLookup(CacheState.Miss);
        }
    }

    public async Task SetIfAbsentAsync(string shortCode, string originalUrl, CancellationToken cancellationToken)
    {
        if (!connection.IsConnected)
        {
            return;
        }

        try
        {
            await connection.GetDatabase()
                .StringSetAsync(Key(shortCode), originalUrl, CacheTtl, When.NotExists)
                .WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Redis write failed for short code {ShortCode}", shortCode);
        }
    }

    public async Task MarkDeletedAsync(string shortCode, CancellationToken cancellationToken)
    {
        if (!connection.IsConnected)
        {
            logger.LogWarning("Redis is disconnected; invalidation skipped for short code {ShortCode}", shortCode);
            return;
        }

        try
        {
            await connection.GetDatabase()
                .StringSetAsync(Key(shortCode), DeletedMarker, CacheTtl)
                .WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Redis invalidation failed for short code {ShortCode}", shortCode);
        }
    }

    private static string Key(string shortCode) => $"url:{shortCode}";

    private static bool IsCacheFailure(Exception exception) =>
        exception is RedisException or TimeoutException or ObjectDisposedException;
}
