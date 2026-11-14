namespace AuthFlow.Api.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tracks consecutive failed login attempts for account lockout protection.
    /// </summary>
    public int AccessFailedCount { get; set; } = 0;

    /// <summary>
    /// UTC timestamp indicating when the temporary account lockout expires.
    /// Null if the account is not locked out.
    /// </summary>
    public DateTime? LockoutEndUtc { get; set; }

    /// <summary>
    /// Navigation property for refresh tokens issued to this user.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
