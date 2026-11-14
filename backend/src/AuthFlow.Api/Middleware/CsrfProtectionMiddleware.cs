using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AuthFlow.Api.Middleware;

public class CsrfProtectionMiddleware
{
    private static readonly HashSet<string> ProtectedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/refresh",
        "/api/auth/logout"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfProtectionMiddleware> _logger;
    private readonly HashSet<string> _allowedOrigins;

    public CsrfProtectionMiddleware(
        RequestDelegate next,
        ILogger<CsrfProtectionMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;

        var configuredOrigins = configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        _allowedOrigins = new HashSet<string>(configuredOrigins, StringComparer.OrdinalIgnoreCase)
        {
            "http://localhost:5173",
            "http://localhost:5202",
            "https://localhost:7034",
            "http://127.0.0.1:5173",
            "http://127.0.0.1:5202"
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        // Only enforce CSRF protection on state-changing requests to cookie-authenticated endpoints
        if (HttpMethods.IsPost(method) && ProtectedPaths.Contains(path))
        {
            // 1. Verify presence of custom anti-CSRF request header
            var hasCustomHeader = context.Request.Headers.ContainsKey("X-Requested-With") ||
                                  context.Request.Headers.ContainsKey("X-AuthFlow-Client") ||
                                  context.Request.Headers.ContainsKey("X-CSRF-Token");

            if (!hasCustomHeader)
            {
                _logger.LogWarning("CSRF validation rejected: Missing required custom header on {Path}", path);
                await WriteProblemResponseAsync(context, StatusCodes.Status403Forbidden,
                    "CSRF Validation Failed",
                    "Missing required security header (e.g. 'X-Requested-With' or 'X-AuthFlow-Client').");
                return;
            }

            // 2. Verify Origin or Referer header
            var originHeader = context.Request.Headers.Origin.FirstOrDefault();
            var refererHeader = context.Request.Headers.Referer.FirstOrDefault();

            string? incomingOrigin = null;

            if (!string.IsNullOrWhiteSpace(originHeader))
            {
                incomingOrigin = originHeader.TrimEnd('/');
            }
            else if (!string.IsNullOrWhiteSpace(refererHeader) && Uri.TryCreate(refererHeader, UriKind.Absolute, out var refererUri))
            {
                incomingOrigin = $"{refererUri.Scheme}://{refererUri.Authority}".TrimEnd('/');
            }

            if (string.IsNullOrWhiteSpace(incomingOrigin))
            {
                _logger.LogWarning("CSRF validation rejected: Missing Origin and Referer header on {Path}", path);
                await WriteProblemResponseAsync(context, StatusCodes.Status403Forbidden,
                    "CSRF Validation Failed",
                    "Missing Origin or Referer browser security header.");
                return;
            }

            // Also allow same-origin requests where incoming origin matches Host
            var hostHeader = context.Request.Host.Value;
            var isSameHost = !string.IsNullOrEmpty(hostHeader) && incomingOrigin.EndsWith(hostHeader, StringComparison.OrdinalIgnoreCase);

            var isTrustedOrigin = _allowedOrigins.Contains(incomingOrigin) || isSameHost;

            if (!isTrustedOrigin)
            {
                _logger.LogWarning("CSRF validation rejected: Untrusted origin '{Origin}' on {Path}", incomingOrigin, path);
                await WriteProblemResponseAsync(context, StatusCodes.Status403Forbidden,
                    "CSRF Validation Failed",
                    "The request origin is not authorized.");
                return;
            }
        }

        await _next(context);
    }

    private static Task WriteProblemResponseAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        var json = JsonSerializer.Serialize(problemDetails);
        return context.Response.WriteAsync(json);
    }
}
