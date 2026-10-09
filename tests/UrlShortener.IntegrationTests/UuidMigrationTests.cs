using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class UuidMigrationTests
{
    [Fact]
    public async Task Existing_account_and_owned_link_keep_their_relationship()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<UrlShortenerDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var database = new UrlShortenerDbContext(options);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009023854_AddAccountCredentials");
        await database.Database.ExecuteSqlRawAsync("""
            INSERT INTO users (id, name, email, created_at)
            VALUES (123, 'Existing User', 'existing@example.com', now());
            INSERT INTO urls (id, short_code, original_url, management_token_hash, user_id, created_at)
            VALUES (100000000001, 'oldcode', 'https://example.com/old', decode(repeat('00', 32), 'hex'), 123, now());
            """);

        await migrator.MigrateAsync();
        var user = await database.Users.SingleAsync(item => item.Email == "existing@example.com");
        var link = await database.Urls.SingleAsync(item => item.ShortCode == "oldcode");
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(user.Id, link.UserId);
        Assert.Equal(0, link.ClickCount);
        Assert.True(await database.ReservedShortCodes.AnyAsync(item => item.Code == "oldcode"));
    }
}
