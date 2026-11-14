namespace AuthFlow.Api.Models;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "AuthFlow.Api";
    public string Audience { get; set; } = "AuthFlow.Client";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Secret))
        {
            throw new InvalidOperationException("JWT Secret is not configured. Configure 'JwtSettings:Secret' via UserSecrets or Environment Variables.");
        }

        if (Secret.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters (256 bits) long to ensure cryptographic security.");
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("JWT Issuer cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("JWT Audience cannot be empty.");
        }

        if (AccessTokenExpirationMinutes <= 0)
        {
            throw new InvalidOperationException("AccessTokenExpirationMinutes must be greater than zero.");
        }

        if (RefreshTokenExpirationDays <= 0)
        {
            throw new InvalidOperationException("RefreshTokenExpirationDays must be greater than zero.");
        }
    }
}
