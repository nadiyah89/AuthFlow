using AuthFlow.Api.DTOs;
using AuthFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuthFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string RefreshTokenCookieName = "refreshToken";
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        var result = await _authService.RegisterAsync(request);

        if (!result.Succeeded)
        {
            return CreateProblemResult(result.StatusCode, result.ErrorTitle!, result.ErrorDetail!);
        }

        if (!string.IsNullOrEmpty(result.RawRefreshToken) && result.RefreshTokenExpiresAtUtc.HasValue)
        {
            AppendRefreshTokenCookie(result.RawRefreshToken, result.RefreshTokenExpiresAtUtc.Value);
        }

        return StatusCode(StatusCodes.Status201Created, result.Response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var result = await _authService.LoginAsync(request);

        if (!result.Succeeded)
        {
            return CreateProblemResult(result.StatusCode, result.ErrorTitle!, result.ErrorDetail!);
        }

        if (!string.IsNullOrEmpty(result.RawRefreshToken) && result.RefreshTokenExpiresAtUtc.HasValue)
        {
            AppendRefreshTokenCookie(result.RawRefreshToken, result.RefreshTokenExpiresAtUtc.Value);
        }

        return Ok(result.Response);
    }

    private void AppendRefreshTokenCookie(string token, DateTime expiresAtUtc)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps || !HttpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = expiresAtUtc
        };

        Response.Cookies.Append(RefreshTokenCookieName, token, cookieOptions);
    }

    private ObjectResult CreateProblemResult(int statusCode, string title, string detail)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = Request.Path
        };

        return StatusCode(statusCode, problemDetails);
    }
}
