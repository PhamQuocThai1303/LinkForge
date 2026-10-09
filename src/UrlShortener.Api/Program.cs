using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using System.Threading.RateLimiting;
using Serilog;
using UrlShortener.Api;
using UrlShortener.Application;
using UrlShortener.Domain;
using UrlShortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<UrlService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
var protection = builder.Services.AddDataProtection().SetApplicationName("LinkForge");
var keyPath = builder.Configuration["Auth:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    Directory.CreateDirectory(keyPath);
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

var authentication = builder.Services.AddAuthentication(AuthEndpoints.SessionScheme)
    .AddCookie(AuthEndpoints.SessionScheme, options =>
    {
        options.Cookie.Name = "linkforge.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddCookie(AuthEndpoints.ExternalScheme, options =>
    {
        options.Cookie.Name = "linkforge.external";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

authentication.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
{
    options.ClientId = "not-configured";
    options.ClientSecret = "not-configured";
    options.SignInScheme = AuthEndpoints.ExternalScheme;
    options.CallbackPath = "/signin-google";
    options.ClaimActions.MapJsonKey("google_verified_email", "email_verified");
    options.ClaimActions.MapJsonKey("google_picture", "picture");
    options.Events.OnRemoteFailure = context =>
    {
        context.Response.Redirect("/app/login.html?error=google-failed");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});
builder.Services.AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var clientId = configuration["Auth:Google:ClientId"];
        var clientSecret = configuration["Auth:Google:ClientSecret"];
        options.ClientId = string.IsNullOrWhiteSpace(clientId) ? "not-configured" : clientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? "not-configured" : clientSecret;
    });
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

await app.ApplyPendingMigrationsAsync();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseFileServer(new FileServerOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "app")),
    RequestPath = "/app",
    EnableDefaultFiles = true
});
// Let static /app files run before the catch-all /{shortCode} endpoint is selected.
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapRootEndpoints();
app.MapUrlEndpoints();
app.MapAuthEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.Run();

public partial class Program;
