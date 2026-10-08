using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using UrlShortener.Api;
using UrlShortener.Application;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class RedisRedirectTests
{
    [Fact]
    public async Task Redirect_uses_cache_falls_back_to_postgres_and_invalidates_delete()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await using var redis = new RedisBuilder("redis:8-alpine").Build();
        await Task.WhenAll(database.StartAsync(), redis.StartAsync());

        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = database.GetConnectionString(),
                    ["Redis:ConnectionString"] = redis.GetConnectionString(),
                    ["ShortUrls:BaseUrl"] = "http://localhost:5000"
                }));
        });
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        await context.Database.MigrateAsync();
        Assert.IsType<RedisCacheService>(application.Services.GetRequiredService<ICacheService>());
        using var redisConnection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var cache = redisConnection.GetDatabase();

        var cached = await CreateAsync(client, "https://example.com/cached");
        var cachedPath = $"/{cached.ShortCode}";
        var cachedKey = $"url:{cached.ShortCode}";

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(cachedPath)).StatusCode);
        Assert.Equal(cached.OriginalUrl, (string?)await cache.StringGetAsync(cachedKey));
        Assert.InRange((await cache.KeyTimeToLiveAsync(cachedKey))!.Value.TotalMinutes, 59, 60);

        await cache.KeyDeleteAsync(cachedKey);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(cachedPath)).StatusCode);
        Assert.Equal(cached.OriginalUrl, (string?)await cache.StringGetAsync(cachedKey));

        await context.Urls.Where(url => url.ShortCode == cached.ShortCode).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(cachedPath)).StatusCode);
        await cache.KeyDeleteAsync(cachedKey);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(cachedPath)).StatusCode);

        var deleted = await CreateAsync(client, "https://example.com/deleted");
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync($"/{deleted.ShortCode}")).StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/urls/{deleted.ShortCode}");
        delete.Headers.Add("X-Management-Token", deleted.ManagementToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
        Assert.Equal("deleted", (string?)await cache.StringGetAsync($"url:{deleted.ShortCode}"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/{deleted.ShortCode}")).StatusCode);

        var fallback = await CreateAsync(client, "https://example.com/fallback");
        await redis.StopAsync();
        var fallbackResponse = await client.GetAsync($"/{fallback.ShortCode}");
        Assert.Equal(HttpStatusCode.Redirect, fallbackResponse.StatusCode);
        Assert.Equal(fallback.OriginalUrl, fallbackResponse.Headers.Location?.ToString());
    }

    private static async Task<CreatedResponse> CreateAsync(HttpClient client, string url)
    {
        var response = await client.PostAsJsonAsync("/api/v1/urls", new { url });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CreatedResponse>())!;
        return body with { OriginalUrl = url };
    }

    private sealed record CreatedResponse(
        string ShortCode,
        string ManagementToken,
        string OriginalUrl = "");
}
