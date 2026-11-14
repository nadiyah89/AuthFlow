using AuthFlow.Api.Data;
using AuthFlow.Api.DTOs;
using AuthFlow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthFlow.Api.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAccessAttempts = 5;
    private const int LockoutDurationMinutes = 15;

    // Static dummy hash used to neutralize timing attacks when an email does not exist
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), "DummyPassword123!");

    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher,
        ILogger<AuthService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequestDto request)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        // 1. Check if email already exists
        var existingUser = await _context.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail);
        if (existingUser)
        {
            return AuthResult.Failure(
                StatusCodes.Status409Conflict,
                "Duplicate Email",
                "An account with this email address already exists.");
        }

        // 2. Instantiate and hash password
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AccessFailedCount = 0,
            LockoutEndUtc = null
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        // 3. Generate initial session refresh token
        var (rawRefreshToken, tokenHash, expiresAtUtc) = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = null,
            ReplacedByTokenId = null
        };

        _context.Users.Add(user);
        _context.RefreshTokens.Add(refreshTokenEntity);

        // 4. Save to PostgreSQL with concurrency duplicate protection
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            _logger.LogWarning("Concurrent duplicate registration collision intercepted for email: {Email}", normalizedEmail);
            return AuthResult.Failure(
                StatusCodes.Status409Conflict,
                "Duplicate Email",
                "An account with this email address already exists.");
        }

        // 5. Generate short-lived JWT access token
        var accessToken = _tokenService.GenerateAccessToken(user);

        var responseDto = new AuthResponseDto
        {
            AccessToken = accessToken,
            ExpiresInMinutes = 15,
            User = new UserProfileDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAtUtc = user.CreatedAtUtc
            }
        };

        return AuthResult.Success(responseDto, rawRefreshToken, expiresAtUtc, StatusCodes.Status201Created);
    }

    public async Task<AuthResult> LoginAsync(LoginRequestDto request)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        // 1. Look up user
        var user = await _context.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        // 2. Prevent account enumeration: if user does not exist, perform dummy hash check
        if (user == null)
        {
            _passwordHasher.VerifyHashedPassword(new User(), DummyHash, request.Password);
            return AuthResult.Failure(
                StatusCodes.Status401Unauthorized,
                "Invalid Credentials",
                "Invalid email or password.");
        }

        // 3. Check if account is inactive
        if (!user.IsActive)
        {
            return AuthResult.Failure(
                StatusCodes.Status403Forbidden,
                "Account Inactive",
                "This account is currently inactive. Please contact support.");
        }

        // 4. Check account lockout status
        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > DateTime.UtcNow)
        {
            var remainingMinutes = Math.Ceiling((user.LockoutEndUtc.Value - DateTime.UtcNow).TotalMinutes);
            return AuthResult.Failure(
                StatusCodes.Status423Locked,
                "Account Locked",
                $"Account is temporarily locked due to repeated failed login attempts. Please try again in {remainingMinutes} minute(s).");
        }

        // 5. Verify password
        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            user.UpdatedAtUtc = DateTime.UtcNow;

            if (user.AccessFailedCount >= MaxFailedAccessAttempts)
            {
                user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
                await _context.SaveChangesAsync();

                _logger.LogWarning("Account locked due to consecutive failed logins: UserId={UserId}", user.Id);
                return AuthResult.Failure(
                    StatusCodes.Status423Locked,
                    "Account Locked",
                    $"Account is temporarily locked due to {MaxFailedAccessAttempts} failed login attempts. Please try again in {LockoutDurationMinutes} minutes.");
            }

            await _context.SaveChangesAsync();
            return AuthResult.Failure(
                StatusCodes.Status401Unauthorized,
                "Invalid Credentials",
                "Invalid email or password.");
        }

        // 6. Login succeeded: Reset failed attempts and lockout
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;

        // 7. Issue new refresh token
        var (rawRefreshToken, tokenHash, expiresAtUtc) = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = null,
            ReplacedByTokenId = null
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync();

        // 8. Generate JWT access token
        var accessToken = _tokenService.GenerateAccessToken(user);

        var responseDto = new AuthResponseDto
        {
            AccessToken = accessToken,
            ExpiresInMinutes = 15,
            User = new UserProfileDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAtUtc = user.CreatedAtUtc
            }
        };

        return AuthResult.Success(responseDto, rawRefreshToken, expiresAtUtc, StatusCodes.Status200OK);
    }

    public async Task<AuthResult> RefreshTokenAsync(string? rawRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return AuthResult.Failure(
                StatusCodes.Status401Unauthorized,
                "Invalid Token",
                "Refresh token was not provided.");
        }

        var tokenHash = _tokenService.HashToken(rawRefreshToken);

        var executionStrategy = _context.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            // Row-level lock via SELECT ... FOR UPDATE ensures strict concurrency protection
            var tokenRecord = await _context.RefreshTokens
                .FromSqlInterpolated($"SELECT * FROM \"RefreshTokens\" WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
                .SingleOrDefaultAsync();

            if (tokenRecord == null)
            {
                return AuthResult.Failure(
                    StatusCodes.Status401Unauthorized,
                    "Invalid Token",
                    "Invalid or expired refresh token.");
            }

            // REUSE DETECTION: An already revoked token is being presented again!
            if (tokenRecord.RevokedAtUtc.HasValue)
            {
                _logger.LogWarning("Refresh token reuse detected! TokenId={TokenId}, FamilyId={FamilyId}. Revoking entire token family.",
                    tokenRecord.Id, tokenRecord.FamilyId);

                // Invalidate all tokens in the entire family lineage immediately
                await _context.RefreshTokens
                    .Where(t => t.FamilyId == tokenRecord.FamilyId && t.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow));

                await transaction.CommitAsync();

                return AuthResult.Failure(
                    StatusCodes.Status401Unauthorized,
                    "Invalid Token",
                    "Invalid or expired refresh token.");
            }

            // Expiry check
            if (tokenRecord.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return AuthResult.Failure(
                    StatusCodes.Status401Unauthorized,
                    "Invalid Token",
                    "Invalid or expired refresh token.");
            }

            // Verify user account exists and is active
            var user = await _context.Users.FindAsync(tokenRecord.UserId);
            if (user == null || !user.IsActive)
            {
                return AuthResult.Failure(
                    StatusCodes.Status401Unauthorized,
                    "Invalid Token",
                    "User account is inactive or not found.");
            }

            // TOKEN ROTATION:
            // 1. Generate successor token
            var (nextRaw, nextHash, nextExpiry) = _tokenService.GenerateRefreshToken();

            var successorToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = nextHash,
                FamilyId = tokenRecord.FamilyId, // Maintains rotation lineage
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = nextExpiry,
                RevokedAtUtc = null,
                ReplacedByTokenId = null
            };

            // 2. Revoke current token and link successor
            tokenRecord.RevokedAtUtc = DateTime.UtcNow;
            tokenRecord.ReplacedByTokenId = successorToken.Id;

            _context.RefreshTokens.Add(successorToken);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 3. Generate new JWT access token
            var accessToken = _tokenService.GenerateAccessToken(user);

            var responseDto = new AuthResponseDto
            {
                AccessToken = accessToken,
                ExpiresInMinutes = 15,
                User = new UserProfileDto
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    CreatedAtUtc = user.CreatedAtUtc
                }
            };

            return AuthResult.Success(responseDto, nextRaw, nextExpiry, StatusCodes.Status200OK);
        });
    }

    public async Task<AuthResult> LogoutAsync(string? rawRefreshToken)
    {
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var tokenHash = _tokenService.HashToken(rawRefreshToken);
            var tokenRecord = await _context.RefreshTokens
                .SingleOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (tokenRecord != null && !tokenRecord.RevokedAtUtc.HasValue)
            {
                tokenRecord.RevokedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Refresh token revoked during logout: TokenId={TokenId}", tokenRecord.Id);
            }
        }

        // Always return success even if cookie was missing or expired (idempotent logout)
        return AuthResult.Success(
            new AuthResponseDto { AccessToken = string.Empty },
            string.Empty,
            DateTime.UtcNow,
            StatusCodes.Status200OK);
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(Guid userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive)
        {
            return null;
        }

        return new UserProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
