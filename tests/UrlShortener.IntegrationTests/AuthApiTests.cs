using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using UrlShortener.Api;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class AuthApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine").Build();
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
                    ["ShortUrls:BaseUrl"] = "http://localhost:5000",
                    ["Auth:Google:ClientId"] = "",
                    ["Auth:Google:ClientSecret"] = ""
                }));
        });
        client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = application.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await application.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task Signup_login_logout_and_authenticated_creation()
    {
        var csrf = await GetCsrfAsync();
        using var signup = await PostAsync("signup", new
        {
            name = "  Alice  ", email = "  ALICE@example.com ", password = "long-secure-password"
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var user = await signup.Content.ReadFromJsonAsync<AuthUserResponse>();
        Assert.Equal("Alice", user?.Name);
        Assert.Equal("alice@example.com", user?.Email);

        using var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var create = await client.PostAsJsonAsync("/api/v1/urls", new { url = "https://example.com/auth-owned" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreateShortUrlResponse>();
        using (var scope = application.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
            var entry = await context.Urls.SingleAsync(item => item.ShortCode == created!.ShortCode);
            Assert.Equal(user!.Id, entry.UserId);
        }

        csrf = await GetCsrfAsync();
        using var logout = await PostAsync("logout", new { }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        csrf = await GetCsrfAsync();
        using var invalid = await PostAsync("login", new { email = "alice@example.com", password = "wrong-password" }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        using var login = await PostAsync("login", new { email = "ALICE@example.com", password = "long-secure-password" }, csrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Rejects_duplicate_and_invalid_signup_and_missing_csrf()
    {
        using var missingCsrf = await client.PostAsJsonAsync("/api/v1/auth/signup", new
        {
            name = "Bob", email = "bob@example.com", password = "long-secure-password"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        var csrf = await GetCsrfAsync();
        using var invalid = await PostAsync("signup", new
        {
            name = "B", email = "invalid", password = "short"
        }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var first = await PostAsync("signup", new
        {
            name = "Bob", email = "bob@example.com", password = "long-secure-password"
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        csrf = await GetCsrfAsync();
        using var duplicate = await PostAsync("signup", new
        {
            name = "Other Bob", email = "BOB@example.com", password = "another-secure-password"
        }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Google_provider_reports_unconfigured()
    {
        var providers = await client.GetFromJsonAsync<Dictionary<string, bool>>("/api/v1/auth/providers");
        Assert.False(providers!["google"]);
        using var challenge = await client.GetAsync("/api/v1/auth/google");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, challenge.StatusCode);
    }

    [Fact]
    public async Task Repeated_login_attempts_are_rate_limited()
    {
        var csrf = await GetCsrfAsync();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await PostAsync("login", new
            {
                email = "absent@example.com", password = "wrong-password"
            }, csrf);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var limited = await PostAsync("login", new
        {
            email = "absent@example.com", password = "wrong-password"
        }, csrf);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Configured_google_challenge_redirects_to_google_with_callback()
    {
        using var configured = application.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Google:ClientId"] = "test-client-id.apps.googleusercontent.com",
                    ["Auth:Google:ClientSecret"] = "test-secret"
                })));
        using var browser = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var providers = await browser.GetFromJsonAsync<Dictionary<string, bool>>("/api/v1/auth/providers");
        Assert.True(providers!["google"]);

        using var challenge = await browser.GetAsync("/api/v1/auth/google");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var location = challenge.Headers.Location!.ToString();
        Assert.StartsWith("https://accounts.google.com/", location);
        Assert.Contains("client_id=test-client-id.apps.googleusercontent.com", location);
        Assert.Contains("redirect_uri=", location);
        Assert.Contains("signin-google", location);
        Assert.Contains("state=", location);
    }

    [Fact]
    public async Task Google_callback_creates_account_and_signs_in()
    {
        using var configured = application.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Google:ClientId"] = "test-client-id.apps.googleusercontent.com",
                    ["Auth:Google:ClientSecret"] = "test-secret"
                }));
            builder.ConfigureTestServices(services => services.PostConfigure<GoogleOptions>(
                GoogleDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(new FakeGoogleHandler())));
        });
        using var browser = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var challenge = await browser.GetAsync("/api/v1/auth/google");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        Assert.NotEmpty(state);

        using var callback = await browser.GetAsync($"/signin-google?code=fake-code&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/api/v1/auth/google/complete", callback.Headers.Location?.ToString());

        using var complete = await browser.GetAsync(callback.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        Assert.Equal("/app/", complete.Headers.Location?.ToString());

        var user = await browser.GetFromJsonAsync<AuthUserResponse>("/api/v1/auth/me");
        Assert.Equal("google.qa@example.com", user?.Email);
        using var scope = application.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>()
            .Users.SingleAsync(item => item.Id == user!.Id);
        Assert.Equal("google-subject-123", stored.GoogleSubject);
        Assert.Null(stored.PasswordHash);
    }

    [Fact]
    public async Task Google_does_not_link_an_existing_password_account_by_email()
    {
        var csrf = await GetCsrfAsync();
        using var signup = await PostAsync("signup", new
        {
            name = "Password account", email = "google.qa@example.com", password = "long-secure-password"
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);

        using var configured = application.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Google:ClientId"] = "test-client-id.apps.googleusercontent.com",
                    ["Auth:Google:ClientSecret"] = "test-secret"
                }));
            builder.ConfigureTestServices(services => services.PostConfigure<GoogleOptions>(
                GoogleDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(new FakeGoogleHandler())));
        });
        using var browser = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var challenge = await browser.GetAsync("/api/v1/auth/google");
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        using var callback = await browser.GetAsync($"/signin-google?code=fake-code&state={Uri.EscapeDataString(state)}");
        using var complete = await browser.GetAsync(callback.Headers.Location);
        Assert.Equal("/app/login.html?error=account-exists", complete.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);

        using var scope = application.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>()
            .Users.SingleAsync(item => item.Email == "google.qa@example.com");
        Assert.Null(stored.GoogleSubject);
    }

    private sealed class FakeGoogleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath.Contains("userinfo", StringComparison.Ordinal)
                ? """{"id":"google-subject-123","email":"google.qa@example.com","email_verified":true,"name":"Google QA"}"""
                : """{"access_token":"fake-access-token","token_type":"Bearer","expires_in":3600}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, new MediaTypeHeaderValue("application/json"))
            });
        }
    }

    private async Task<string> GetCsrfAsync()
    {
        var response = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/v1/auth/csrf");
        return response!["token"];
    }

    private Task<HttpResponseMessage> PostAsync(string route, object body, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{route}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return client.SendAsync(request);
    }
}
