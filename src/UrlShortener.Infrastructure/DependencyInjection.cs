using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using UrlShortener.Application;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<UrlShortenerDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.")));
        services.AddScoped<IUrlRepository, PostgresUrlRepository>();
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var redisConnection = provider.GetRequiredService<IConfiguration>()["Redis:ConnectionString"]
                ?? throw new InvalidOperationException("Redis:ConnectionString is required.");
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 1000;
            options.SyncTimeout = 1000;
            options.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<NoopCacheService>();
        services.AddSingleton<RedisCacheService>();
        services.AddSingleton<ICacheService>(provider =>
        {
            var redisConnection = provider.GetRequiredService<IConfiguration>()["Redis:ConnectionString"];
            return string.IsNullOrWhiteSpace(redisConnection)
                ? provider.GetRequiredService<NoopCacheService>()
                : provider.GetRequiredService<RedisCacheService>();
        });
        return services;
    }
}
