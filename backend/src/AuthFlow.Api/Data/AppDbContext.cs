using AuthFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthFlow.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User Entity Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");

            entity.HasKey(u => u.Id);

            entity.Property(u => u.FullName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(u => u.Email)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(u => u.NormalizedEmail)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(u => u.PasswordHash)
                  .IsRequired();

            entity.Property(u => u.IsActive)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(u => u.CreatedAtUtc)
                  .IsRequired();

            entity.Property(u => u.UpdatedAtUtc)
                  .IsRequired();

            entity.Property(u => u.AccessFailedCount)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(u => u.LockoutEndUtc)
                  .IsRequired(false);

            // Unique index on NormalizedEmail for duplicate-email protection
            entity.HasIndex(u => u.NormalizedEmail)
                  .IsUnique()
                  .HasDatabaseName("IX_Users_NormalizedEmail");

            // Relationship to RefreshTokens
            entity.HasMany(u => u.RefreshTokens)
                  .WithOne(rt => rt.User)
                  .HasForeignKey(rt => rt.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // RefreshToken Entity Configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");

            entity.HasKey(rt => rt.Id);

            entity.Property(rt => rt.TokenHash)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(rt => rt.FamilyId)
                  .IsRequired();

            entity.Property(rt => rt.CreatedAtUtc)
                  .IsRequired();

            entity.Property(rt => rt.ExpiresAtUtc)
                  .IsRequired();

            entity.Property(rt => rt.RevokedAtUtc)
                  .IsRequired(false);

            entity.Property(rt => rt.ReplacedByTokenId)
                  .IsRequired(false);

            // Unique index on TokenHash to guarantee token uniqueness
            entity.HasIndex(rt => rt.TokenHash)
                  .IsUnique()
                  .HasDatabaseName("IX_RefreshTokens_TokenHash");

            // Index on UserId for fast token retrieval by user
            entity.HasIndex(rt => rt.UserId)
                  .HasDatabaseName("IX_RefreshTokens_UserId");

            // Index on FamilyId for token lineage lookup during rotation/revocation
            entity.HasIndex(rt => rt.FamilyId)
                  .HasDatabaseName("IX_RefreshTokens_FamilyId");

            // Ignore computed getter-only helper properties
            entity.Ignore(rt => rt.IsRevoked);
            entity.Ignore(rt => rt.IsExpired);
            entity.Ignore(rt => rt.IsActive);
        });
    }
}
