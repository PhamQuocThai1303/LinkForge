using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using UrlShortener.Api;
using UrlShortener.Application;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class UrlApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .Build();

    private WebApplicationFactory<Program> application = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = database.GetConnectionString(),
                    ["ShortUrls:BaseUrl"] = "http://localhost:5000"
                }));
        });
        client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await application.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task Create_persists_a_unique_code_and_redirects()
    {
        var first = await CreateAsync("https://example.com/path?q=1");
        var second = await CreateAsync("https://example.com/path?q=1");

        Assert.Equal(7, first.ShortCode.Length);
        Assert.NotEqual(first.ShortCode, second.ShortCode);
        Assert.Equal($"http://localhost:5000/{first.ShortCode}", first.ShortUrl);
        Assert.NotEmpty(first.ManagementToken);

        using (var scope = application.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
            var stored = await context.Urls.SingleAsync(url => url.ShortCode == first.ShortCode);
            Assert.Equal(32, stored.ManagementTokenHash.Length);
            Assert.NotEqual(first.ManagementToken, Convert.ToBase64String(stored.ManagementTokenHash));
        }

        var redirect = await client.GetAsync($"/{first.ShortCode}");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/path?q=1", redirect.Headers.Location?.ToString());

        var info = await client.GetFromJsonAsync<UrlInfo>($"/api/v1/urls/{first.ShortCode}");
        Assert.Equal(first.ShortCode, info?.ShortCode);
        Assert.Equal("https://example.com/path?q=1", info?.OriginalUrl);
        var infoJson = await client.GetStringAsync($"/api/v1/urls/{first.ShortCode}");
        Assert.DoesNotContain("managementToken", infoJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Swagger_document_describes_the_api()
    {
        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/v1/urls", swagger, StringComparison.Ordinal);
        Assert.Contains("/{shortCode}", swagger, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_endpoints_report_liveness_and_database_readiness()
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Root_endpoint_reports_service_status()
    {
        var response = await client.GetFromJsonAsync<ServiceInfoResponse>("/");

        Assert.Equal("LinkForge", response?.Service);
        Assert.Equal("ok", response?.Status);
        Assert.Equal("/api/v1/urls", response?.ApiBasePath);
    }

    [Fact]
    public async Task Client_page_and_assets_are_served()
    {
        var page = await client.GetAsync("/app/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("LinkForge", await page.Content.ReadAsStringAsync());

        var stylesheet = await client.GetAsync("/app/styles.css");
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);

        var script = await client.GetAsync("/app/app.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("/api/v1/urls", await script.Content.ReadAsStringAsync());

        foreach (var route in new[] { "/app/login.html", "/app/signup.html", "/app/auth.css", "/app/auth.js" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://localhost/private")]
    [InlineData("https://user:pass@example.com/")]
    public async Task Invalid_url_is_rejected(string url)
    {
        var response = await client.PostAsJsonAsync("/api/v1/urls", new { url });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_code_returns_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ZZZZZZZ")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/urls/ZZZZZZZ")).StatusCode);
    }

    [Fact]
    public async Task Delete_requires_the_management_token()
    {
        var created = await CreateAsync("https://example.com/delete-me");
        var path = $"/api/v1/urls/{created.ShortCode}";

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(path)).StatusCode);

        using var wrongRequest = new HttpRequestMessage(HttpMethod.Delete, path);
        wrongRequest.Headers.Add("X-Management-Token", "wrong-token");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrongRequest)).StatusCode);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, path);
        deleteRequest.Headers.Add("X-Management-Token", created.ManagementToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(deleteRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/{created.ShortCode}")).StatusCode);
    }

    [Fact]
    public async Task Database_enforces_unique_short_code()
    {
        var created = await CreateAsync("https://example.com/first");
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        context.Urls.Add(new UrlEntry
        {
            Id = 999_999_999_999,
            ShortCode = created.ShortCode,
            OriginalUrl = "https://example.com/second",
            ManagementTokenHash = new byte[32],
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private async Task<CreateShortUrlResponse> CreateAsync(string url)
    {
        var response = await client.PostAsJsonAsync("/api/v1/urls", new { url });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<CreateShortUrlResponse>())!;
    }
}
