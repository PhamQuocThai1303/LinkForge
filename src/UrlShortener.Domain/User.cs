namespace UrlShortener.Domain;

public sealed class User
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string ApiKeyHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
