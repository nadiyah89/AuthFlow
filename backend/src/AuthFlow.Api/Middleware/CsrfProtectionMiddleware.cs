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
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;

        var configuredOrigins = configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        var origins = new HashSet<string>(
            configuredOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim().TrimEnd('/')),
            StringComparer.OrdinalIgnoreCase);

        // In development, automatically allow local development origins
        if (environment.IsDevelopment())
        {
            origins.Add("http://localhost:5173");
            origins.Add("http://localhost:5202");
            origins.Add("https://localhost:7034");
            origins.Add("http://127.0.0.1:5173");
            origins.Add("http://127.0.0.1:5202");
        }

        _allowedOrigins = origins;
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

            string? rawOriginCandidate = null;

            if (!string.IsNullOrWhiteSpace(originHeader))
            {
                rawOriginCandidate = originHeader.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(refererHeader))
            {
                rawOriginCandidate = refererHeader.Trim();
            }

            if (string.IsNullOrWhiteSpace(rawOriginCandidate))
            {
                _logger.LogWarning("CSRF validation rejected: Missing Origin and Referer header on {Path}", path);
                await WriteProblemResponseAsync(context, StatusCodes.Status403Forbidden,
                    "CSRF Validation Failed",
                    "Missing Origin or Referer browser security header.");
                return;
            }

            if (!Uri.TryCreate(rawOriginCandidate, UriKind.Absolute, out var parsedOriginUri) ||
                (parsedOriginUri.Scheme != Uri.UriSchemeHttp && parsedOriginUri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.LogWarning("CSRF validation rejected: Malformed Origin/Referer header '{Header}' on {Path}", rawOriginCandidate, path);
                await WriteProblemResponseAsync(context, StatusCodes.Status403Forbidden,
                    "CSRF Validation Failed",
                    "Invalid Origin or Referer browser security header.");
                return;
            }

            var canonicalOrigin = $"{parsedOriginUri.Scheme}://{parsedOriginUri.Authority}".TrimEnd('/');

            // Check against configured allowed origins, or verify exact same-origin
            var isExplicitlyAllowed = _allowedOrigins.Contains(canonicalOrigin);
            var isSameOrigin = IsSameOrigin(parsedOriginUri, context.Request);

            if (!isExplicitlyAllowed && !isSameOrigin)
            {
                _logger.LogWarning("CSRF validation rejected: Untrusted origin '{Origin}' on {Path}", canonicalOrigin, path);
                await WriteProblemResponseAsync(context, StatusCodes.Status403Forbidden,
                    "CSRF Validation Failed",
                    "The request origin is not authorized.");
                return;
            }
        }

        await _next(context);
    }

    private static bool IsSameOrigin(Uri originUri, HttpRequest request)
    {
        // 1. Strict Host check (exact case-insensitive match, preventing suffix injection attacks)
        if (!string.Equals(originUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 2. Port check (accounting for default HTTP 80 / HTTPS 443 ports)
        var requestPort = request.Host.Port ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        var originPort = originUri.IsDefaultPort
            ? (string.Equals(originUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : originUri.Port;

        if (requestPort != originPort)
        {
            return false;
        }

        // 3. Scheme check (allow HTTP/HTTPS divergence strictly on local development hosts)
        if (!string.Equals(originUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            var isLocalhost = request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                              request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

            if (!isLocalhost)
            {
                return false;
            }
        }

        return true;
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
