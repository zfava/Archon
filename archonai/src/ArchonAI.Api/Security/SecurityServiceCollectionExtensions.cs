using System.Text;
using System.Threading.RateLimiting;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Services;
using ArchonAI.Governance;
using ArchonAI.Memory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace ArchonAI.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddArchonAISecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        string issuer = jwtSection["Issuer"] ?? "ArchonAI";
        string audience = jwtSection["Audience"] ?? "ArchonAI.Api";

        // Resolve signing key from config or environment (injected by K8s Secret / .env)
        string signingKey = jwtSection["SigningKey"]
            ?? Environment.GetEnvironmentVariable("ARCHONAI_JWT_SIGNING_KEY")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured or too short. Provide Security:Jwt:SigningKey via secure configuration or ARCHONAI_JWT_SIGNING_KEY (>=32 chars).");
        }

        int clockSkewSeconds = 60;
        _ = int.TryParse(jwtSection["ClockSkewSeconds"], out clockSkewSeconds);

        var currentKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

        // Support dual-key validation during rotation rollover
        var validationKeys = new List<SecurityKey> { currentKey };
        string? previousKey = Environment.GetEnvironmentVariable("ARCHONAI_JWT_SIGNING_KEY_PREVIOUS");
        if (!string.IsNullOrWhiteSpace(previousKey) && previousKey.Length >= 32)
        {
            validationKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(previousKey)));
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = currentKey,
                    IssuerSigningKeys = validationKeys,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(Math.Max(0, clockSkewSeconds))
                };
            })
            .AddScheme<ExternalApiKeyAuthOptions, ExternalApiKeyAuthHandler>(
                ExternalApiKeyAuthHandler.SchemeName, _ => { });

        services.AddSingleton<IRbacService, RbacService>();
        services.AddSingleton<IGovernanceService, GovernanceService>();
        services.AddSingleton<ITrustTierService, TrustTierService>();
        services.AddSingleton<IOutcomeLearningService, OutcomeLearningService>();
        services.AddSingleton<IEnterpriseMemoryService, EnterpriseMemoryService>();
        services.AddSingleton<IOperationalTwinService, OperationalTwinService>();
        services.AddSingleton<IScenarioService, ScenarioService>();
        services.AddSingleton<IExceptionIntelligenceService, ExceptionIntelligenceService>();
        services.AddSingleton<IExecutiveCommandService, ExecutiveCommandService>();
        services.AddSingleton<IGatedActionExecutor, GatedActionExecutor>();
        services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, TenantMatchRequirementHandler>();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"))
            .AddPolicy("OperatorOrAdmin", policy => policy.RequireRole("Operator", "Admin"))
            .AddPolicy("ViewerOrAbove", policy => policy.RequireRole("Viewer", "Operator", "Admin"))
            .AddPolicy("AgentRead", policy => policy.Requirements.Add(new PermissionRequirement("agents:read")))
            .AddPolicy("AgentWrite", policy => policy.Requirements.Add(new PermissionRequirement("agents:write")))
            .AddPolicy("ConnectorAccess", policy => policy.Requirements.Add(new PermissionRequirement("connectors:execute")))
            .AddPolicy("PolicyManagement", policy => policy.Requirements.Add(new PermissionRequirement("policy:write")))
            .AddPolicy("RbacManagement", policy => policy.Requirements.Add(new PermissionRequirement("rbac:write")))
            .AddPolicy("GovernanceRead", policy => policy.Requirements.Add(new PermissionRequirement("governance:read")))
            .AddPolicy("GovernanceWrite", policy => policy.Requirements.Add(new PermissionRequirement("governance:write")))
            .AddPolicy("GovernanceApprove", policy => policy.Requirements.Add(new PermissionRequirement("governance:approve")))
            .AddPolicy("TenantScoped", policy => policy.Requirements.Add(new TenantMatchRequirement()));

        // Parse configurable external API rate limit
        int externalApiPermitLimit = 60;
        var externalSection = configuration.GetSection("ExternalApi");
        if (int.TryParse(externalSection["RateLimitPerMinute"], out var configuredLimit) && configuredLimit > 0)
            externalApiPermitLimit = configuredLimit;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Rate limit exceeded. Retry after the period specified in Retry-After header." },
                    cancellationToken);
            };
            options.AddFixedWindowLimiter("api", limiterOptions =>
            {
                limiterOptions.PermitLimit = 120;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 20;
            });
            // Per-organization sliding window for external API
            options.AddPolicy("external-api", context =>
            {
                var orgId = context.User?.FindFirst("org-id")?.Value ?? "anonymous";
                return RateLimitPartition.GetSlidingWindowLimiter(orgId, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = externalApiPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 5,
                });
            });
        });

        return services;
    }
}
