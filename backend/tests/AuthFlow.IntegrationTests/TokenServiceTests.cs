using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthFlow.Api.Models;
using AuthFlow.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AuthFlow.IntegrationTests;

public class TokenServiceTests
{
    private readonly JwtSettings _jwtSettings;
    private readonly TokenService _tokenService;

    public TokenServiceTests()
    {
        _jwtSettings = new JwtSettings
        {
            Secret = "AuthFlow_Super_Secure_Dev_Secret_Key_2026_Minimum_32_Bytes_Long_Secret!",
            Issuer = "AuthFlow.Api",
            Audience = "AuthFlow.Client",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };

        _tokenService = new TokenService(Options.Create(_jwtSettings));
    }

    // 10. JWT Signature and Claims Validation
    [Fact]
    public void Jwt_Signature_And_Claims_Are_Valid()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "jwt.test@example.com",
            FullName = "JWT Validator User"
        };

        var tokenString = _tokenService.GenerateAccessToken(user);
        Assert.False(string.IsNullOrWhiteSpace(tokenString));

        var principal = _tokenService.ValidateToken(tokenString, validateLifetime: true);
        Assert.NotNull(principal);

        var subClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var emailClaim = principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        var nameClaim = principal.FindFirst("full_name")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

        Assert.Equal(user.Id.ToString(), subClaim);
        Assert.Equal(user.Email, emailClaim);
        Assert.Equal(user.FullName, nameClaim);
        Assert.NotNull(jtiClaim);
    }

    // 11. Expired JWT Rejection
    [Fact]
    public void Expired_Jwt_Is_Rejected()
    {
        var expiredTokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "expired@example.com")
            }),
            // Set NotBefore and Expires in the past to simulate an expired token
            NotBefore = DateTime.UtcNow.AddMinutes(-30),
            IssuedAt = DateTime.UtcNow.AddMinutes(-30),
            Expires = DateTime.UtcNow.AddMinutes(-10),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret)),
                SecurityAlgorithms.HmacSha256)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var expiredToken = tokenHandler.CreateToken(expiredTokenDescriptor);
        var expiredTokenString = tokenHandler.WriteToken(expiredToken);

        // Validation with validateLifetime: true must return null (rejected)
        var principal = _tokenService.ValidateToken(expiredTokenString, validateLifetime: true);
        Assert.Null(principal);

        // Without lifetime check, signature and claims pass
        var principalWithoutLifetime = _tokenService.ValidateToken(expiredTokenString, validateLifetime: false);
        Assert.NotNull(principalWithoutLifetime);
    }
}
