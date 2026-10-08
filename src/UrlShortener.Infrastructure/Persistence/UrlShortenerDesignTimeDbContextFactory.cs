using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrlShortener.Infrastructure.Persistence;

public sealed class UrlShortenerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<UrlShortenerDbContext>
{
    public UrlShortenerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=linkforge;Username=linkforge;Password=unused";
        var options = new DbContextOptionsBuilder<UrlShortenerDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new UrlShortenerDbContext(options);
    }
}
