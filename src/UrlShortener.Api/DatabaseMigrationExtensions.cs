using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyPendingMigrationsAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        if (!app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
