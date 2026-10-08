namespace UrlShortener.Domain;

public sealed class UrlEntry
{
    public long Id { get; set; }
    public required string ShortCode { get; set; }
    public required string OriginalUrl { get; set; }
    public required byte[] ManagementTokenHash { get; set; }
    public long? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
