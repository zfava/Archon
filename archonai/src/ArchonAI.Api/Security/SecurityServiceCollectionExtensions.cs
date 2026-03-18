using System.Text;
using ArchonAI.Core.Interfaces;
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

        var signingSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

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
                    IssuerSigningKey = signingSecurityKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(Math.Max(0, clockSkewSeconds))
                };
            });

        services.AddSingleton<IRbacService, RbacService>();
        services.AddSingleton<IGovernanceService, GovernanceService>();
        services.AddSingleton<ITrustTierService, TrustTierService>();
        services.AddSingleton<IOutcomeLearningService, OutcomeLearningService>();
        services.AddSingleton<IEnterpriseMemoryService, EnterpriseMemoryService>();
        services.AddSingleton<IOperationalTwinService, OperationalTwinService>();
        services.AddSingleton<IScenarioService, ScenarioService>();
        services.AddSingleton<IExceptionIntelligenceService, ExceptionIntelligenceService>();
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

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("api", limiterOptions =>
            {
                limiterOptions.PermitLimit = 120;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 20;
            });
        });

        return services;
    }
}
