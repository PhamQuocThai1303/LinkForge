using UrlShortener.Domain;

namespace UrlShortener.Application;

public interface IUrlRepository
{
    Task<long> NextIdAsync(CancellationToken cancellationToken);
    Task AddAsync(UrlEntry entry, CancellationToken cancellationToken);
    Task<UrlEntry?> FindAsync(string shortCode, CancellationToken cancellationToken);
    Task DeleteAsync(UrlEntry entry, CancellationToken cancellationToken);
}
