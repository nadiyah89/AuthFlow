namespace AuthFlow.Api.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the cryptographically random token string.
    /// The raw token is never stored in the database.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Identifier shared across all tokens generated in a single refresh lineage.
    /// Used for reuse detection and token family revocation.
    /// </summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>
    /// Navigation property to the owning user.
    /// </summary>
    public User User { get; set; } = null!;

    // Computed convenience properties (not mapped to DB columns)
    public bool IsRevoked => RevokedAtUtc.HasValue;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsActive => !IsRevoked && !IsExpired;
}
