using System.Data;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Application;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.Persistence;

public sealed class PostgresUrlRepository(UrlShortenerDbContext dbContext) : IUrlRepository
{
    public async Task<long> NextIdAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('url_ids')";
            return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task AddAsync(UrlEntry entry, CancellationToken cancellationToken)
    {
        dbContext.Urls.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<UrlEntry?> FindAsync(string shortCode, CancellationToken cancellationToken) =>
        dbContext.Urls.AsNoTracking().SingleOrDefaultAsync(url => url.ShortCode == shortCode, cancellationToken);

    public Task DeleteAsync(UrlEntry entry, CancellationToken cancellationToken) =>
        dbContext.Urls.Where(url => url.Id == entry.Id).ExecuteDeleteAsync(cancellationToken);
}
