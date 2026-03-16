using System.Text;
using System.Threading.RateLimiting;
using ArchonAI.Common.Observability;
using ArchonAI.Gateway.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((context, _, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ArchonAI.Gateway")
    .WriteTo.Console());

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ArchonAI.Gateway"))
    .WithTracing(t => t
        .AddSource(Telemetry.ActivitySource.Name)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(m => m
        .AddMeter(Telemetry.Meter.Name)
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

// JWT Authentication
var jwtSection = builder.Configuration.GetSection("Security:Jwt");
string issuer = jwtSection["Issuer"] ?? "ArchonAI";
string audience = jwtSection["Audience"] ?? "ArchonAI.Api";
string signingKey = jwtSection["SigningKey"]
    ?? Environment.GetEnvironmentVariable("ARCHONAI_JWT_SIGNING_KEY")
    ?? string.Empty;

if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
{
    throw new InvalidOperationException(
        "JWT signing key is not configured or too short. Provide Security:Jwt:SigningKey or ARCHONAI_JWT_SIGNING_KEY (>=32 chars).");
}

int clockSkewSeconds = int.TryParse(jwtSection["ClockSkewSeconds"], out var cs) ? cs : 60;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Max(0, clockSkewSeconds))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("OperatorOrAdmin", policy => policy.RequireRole("Operator", "Admin"));
});

// Tiered Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("standard", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 20;
    });

    options.AddFixedWindowLimiter("admin", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 10;
    });

    options.AddFixedWindowLimiter("connectors", limiter =>
    {
        limiter.PermitLimit = 200;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 30;
    });

    options.AddFixedWindowLimiter("health", limiter =>
    {
        limiter.PermitLimit = 300;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("metrics", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
});

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Gateway metrics
builder.Services.AddSingleton<GatewayMetrics>();

var app = builder.Build();

app.UseSerilogRequestLogging();

// Correlation ID middleware (before everything else)
app.UseMiddleware<CorrelationIdMiddleware>();

// Request logging and metrics
app.UseMiddleware<GatewayRequestMiddleware>();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Health endpoint (no auth required)
app.MapGet("/health", () => Results.Ok(new
{
    service = "ArchonAI.Gateway",
    status = "ok",
    timestamp = DateTimeOffset.UtcNow
})).AllowAnonymous().RequireRateLimiting("health");

// Gateway status
app.MapGet("/gateway/status", (GatewayMetrics metrics) =>
    Results.Ok(metrics.GetSnapshot()))
    .RequireAuthorization("AdminOnly")
    .RequireRateLimiting("admin");

// Prometheus metrics
app.MapPrometheusScrapingEndpoint("/metrics")
    .RequireAuthorization("AdminOnly")
    .RequireRateLimiting("metrics");

// YARP proxy - handles all /api/** routes
app.MapReverseProxy();

app.Run();
