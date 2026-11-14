using AuthFlow.Api.Data;
using AuthFlow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace AuthFlow.IntegrationTests;

public class DatabaseIntegrationTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly List<Guid> _createdUserIds = new();

    public DatabaseIntegrationTests()
    {
        // Build configuration including UserSecrets from the API assembly
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=authflow_dev;Username=postgres;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        _context = new AppDbContext(options);
    }

    public void Dispose()
    {
        // Cleanup test artifacts from PostgreSQL
        if (_createdUserIds.Count > 0)
        {
            var usersToDelete = _context.Users
                .Where(u => _createdUserIds.Contains(u.Id))
                .ToList();

            if (usersToDelete.Count > 0)
            {
                _context.Users.RemoveRange(usersToDelete);
                _context.SaveChanges();
            }
        }
        _context.Dispose();
    }

    [Fact]
    public async Task Can_Connect_To_PostgreSQL()
    {
        var canConnect = await _context.Database.CanConnectAsync();
        Assert.True(canConnect, "Application should successfully connect to the PostgreSQL instance.");
    }

    [Fact]
    public async Task Unique_NormalizedEmail_Constraint_Enforced()
    {
        var uniqueEmail = $"test-{Guid.NewGuid():N}@example.com";
        var normalizedEmail = uniqueEmail.ToUpperInvariant();

        var user1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "First User",
            Email = uniqueEmail,
            NormalizedEmail = normalizedEmail,
            PasswordHash = "hash1",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var user2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Second User",
            Email = uniqueEmail,
            NormalizedEmail = normalizedEmail,
            PasswordHash = "hash2",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(user1);
        await _context.SaveChangesAsync();
        _createdUserIds.Add(user1.Id);

        _context.Users.Add(user2);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        Assert.NotNull(exception.InnerException);
        Assert.IsType<PostgresException>(exception.InnerException);
        var pgEx = (PostgresException)exception.InnerException;
        Assert.Equal("23505", pgEx.SqlState); // 23505 = unique_violation
    }

    [Fact]
    public async Task Unique_RefreshToken_TokenHash_Constraint_Enforced()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Token User",
            Email = $"token-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"TOKEN-{Guid.NewGuid():N}@EXAMPLE.COM",
            PasswordHash = "hash",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        _createdUserIds.Add(user.Id);

        var duplicateTokenHash = $"hash-{Guid.NewGuid():N}";

        var token1 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = duplicateTokenHash,
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        var token2 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = duplicateTokenHash,
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        _context.RefreshTokens.Add(token1);
        await _context.SaveChangesAsync();

        _context.RefreshTokens.Add(token2);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        Assert.NotNull(exception.InnerException);
        Assert.IsType<PostgresException>(exception.InnerException);
        var pgEx = (PostgresException)exception.InnerException;
        Assert.Equal("23505", pgEx.SqlState); // unique_violation
    }

    [Fact]
    public async Task ForeignKey_Cascade_Delete_Enforced()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Cascade User",
            Email = $"cascade-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"CASCADE-{Guid.NewGuid():N}@EXAMPLE.COM",
            PasswordHash = "hash",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = $"hash-{Guid.NewGuid():N}",
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        user.RefreshTokens.Add(token);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Verify token exists in database
        var tokenInDb = await _context.RefreshTokens.FindAsync(token.Id);
        Assert.NotNull(tokenInDb);

        // Delete user
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        // Verify token was cascaded and deleted
        var tokenAfterDelete = await _context.RefreshTokens.FindAsync(token.Id);
        Assert.Null(tokenAfterDelete);
    }

    [Fact]
    public async Task Utc_Timestamp_RoundTrip_Preserves_Utc()
    {
        var nowUtc = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Timestamp User",
            Email = $"timestamp-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"TIMESTAMP-{Guid.NewGuid():N}@EXAMPLE.COM",
            PasswordHash = "hash",
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        _createdUserIds.Add(user.Id);

        // Detach to force fresh read from PostgreSQL
        _context.Entry(user).State = EntityState.Detached;

        var userFromDb = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(userFromDb);
        Assert.Equal(DateTimeKind.Utc, userFromDb.CreatedAtUtc.Kind);
        Assert.True((userFromDb.CreatedAtUtc - nowUtc).Duration() < TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void PasswordHasher_PBKDF2_Hashes_And_Verifies_Correctly()
    {
        var hasher = new PasswordHasher<User>();
        var user = new User { Id = Guid.NewGuid(), Email = "hasher@example.com" };
        var rawPassword = "P@ssword123!Secure";

        // Hash password
        var hashedPassword = hasher.HashPassword(user, rawPassword);

        Assert.False(string.IsNullOrWhiteSpace(hashedPassword));
        Assert.NotEqual(rawPassword, hashedPassword);

        // Verify correct password
        var successResult = hasher.VerifyHashedPassword(user, hashedPassword, rawPassword);
        Assert.Equal(PasswordVerificationResult.Success, successResult);

        // Verify incorrect password fails
        var failureResult = hasher.VerifyHashedPassword(user, hashedPassword, "WrongPassword!");
        Assert.Equal(PasswordVerificationResult.Failed, failureResult);
    }
}
