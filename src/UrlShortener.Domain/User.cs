namespace UrlShortener.Domain;

public sealed class User
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? ApiKeyHash { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
