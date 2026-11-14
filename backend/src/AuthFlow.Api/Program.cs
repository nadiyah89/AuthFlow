using System.Text;
using System.Threading.RateLimiting;
using AuthFlow.Api.Data;
using AuthFlow.Api.Middleware;
using AuthFlow.Api.Models;
using AuthFlow.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Render dynamic PORT support: bind to 0.0.0.0:$PORT if PORT is provided by environment
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// 1. Add services to the container
builder.Services.AddControllers();

// Configure forwarded headers for Render reverse proxy (SSL termination)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1; // Only trust the single reverse-proxy hop from Render's edge proxy
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// 2. Configure and Validate JWT Settings
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
builder.Services.Configure<JwtSettings>(jwtSection);

var jwtSettings = jwtSection.Get<JwtSettings>() ?? new JwtSettings();
jwtSettings.Validate();

// 3. Configure EF Core with PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

// 4. Configure Identity Password Hasher & Services
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// 5. Configure JWT Bearer Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Set to true in production if needed
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// 6. Configure Rate Limiting for Auth Endpoints
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth-limit", policy =>
    {
        policy.PermitLimit = 30;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        policy.QueueLimit = 0;
    });
});

// 7. Configure CORS for frontend access
const string corsPolicy = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173" };

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Apply forwarded headers only in production behind Render reverse proxy
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
}

// 8. Configure HTTP Request Pipeline
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors(corsPolicy);

app.UseMiddleware<CsrfProtectionMiddleware>();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers().RequireRateLimiting("auth-limit");

app.Run();

public partial class Program { }
