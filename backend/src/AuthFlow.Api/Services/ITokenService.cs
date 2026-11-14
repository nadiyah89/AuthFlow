using System.Security.Claims;
using AuthFlow.Api.Models;

namespace AuthFlow.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);

    (string rawToken, string tokenHash, DateTime expiresAtUtc) GenerateRefreshToken();

    string HashToken(string rawToken);

    ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true);
}
