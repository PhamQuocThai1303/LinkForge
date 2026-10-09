using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.Persistence;

public sealed class UrlShortenerDbContext(DbContextOptions<UrlShortenerDbContext> options) : DbContext(options)
{
    public DbSet<UrlEntry> Urls => Set<UrlEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ReservedShortCode> ReservedShortCodes => Set<ReservedShortCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("url_ids").StartsAt(100_000_000_000);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(user => user.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(128);
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash");
            entity.Property(user => user.GoogleSubject).HasColumnName("google_subject").HasMaxLength(255);
            entity.Property(user => user.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(2048);
            entity.HasIndex(user => user.GoogleSubject).IsUnique();
            entity.Property(user => user.CreatedAt).HasColumnName("created_at").IsRequired();
        });

        modelBuilder.Entity<UrlEntry>(entity =>
        {
            entity.ToTable("urls");
            entity.HasKey(url => url.Id);
            entity.Property(url => url.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(url => url.ShortCode).HasColumnName("short_code").HasMaxLength(16).IsRequired();
            entity.HasIndex(url => url.ShortCode).IsUnique();
            entity.Property(url => url.OriginalUrl).HasColumnName("original_url").IsRequired();
            entity.Property(url => url.ManagementTokenHash).HasColumnName("management_token_hash").IsRequired();
            entity.Property(url => url.UserId).HasColumnName("user_id");
            entity.Property(url => url.ClickCount).HasColumnName("click_count").HasDefaultValue(0L).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(url => url.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(url => url.CreatedAt).HasColumnName("created_at").IsRequired();
        });

        modelBuilder.Entity<ReservedShortCode>(entity =>
        {
            entity.ToTable("reserved_short_codes");
            entity.HasKey(code => code.Code);
            entity.Property(code => code.Code).HasColumnName("code").HasMaxLength(16);
        });
    }
}
