using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api;

public sealed class DatabaseHealthCheck(UrlShortenerDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.Urls.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL and URL schema are ready.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL or URL schema is not ready.", exception);
        }
    }
}
