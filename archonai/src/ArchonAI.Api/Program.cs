using Serilog;
using ArchonAI.Agents;
using ArchonAI.Api.Endpoints;
using ArchonAI.Api.Security;
using ArchonAI.Connectors;
using ArchonAI.Core.Interfaces;
using ArchonAI.Infrastructure;
using ArchonAI.Plugins;
using ArchonAI.Registry;
using ArchonAI.AdminAPI;
using ArchonAI.Agents.Finance;
using ArchonAI.Agents.Marketing;
using ArchonAI.Agents.Operations;
using ArchonAI.Agents.Sales;
using ArchonAI.Agents.Support;
using ArchonAI.ControlPlane;
using ArchonAI.Runtime;
using ArchonAI.WorkflowDesigner;
using ArchonAI.AgentRegistry;
using ArchonAI.StrategyLibrary;
using ArchonAI.WorkflowSimulation;
using ArchonAI.ControlPlane.Hubs;
using ArchonAI.Memory;
using ArchonAI.Perception;
using ArchonAI.OrganizationState;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.PolicySimulation;
using ArchonAI.Core.Models.ProofAnalytics;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.Inspection;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using ArchonAI.Migrations;
using ArchonAI.Persistence;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args)
    .AddArchonAIObservability();

builder.Services.AddArchonAISecurity(builder.Configuration);
builder.Services.AddArchonAIInfrastructure();
builder.Services.AddArchonAIConnectors(builder.Configuration);
builder.Services.AddArchonAIAgentTooling();
builder.Services.AddArchonAIPlugins(builder.Configuration);
builder.Services.AddArchonAIRegistry();
builder.Services.AddArchonAIRuntime(builder.Configuration);
builder.Services.AddArchonAIOperations(builder.Configuration);
builder.Services.AddArchonAIFinance(builder.Configuration);
builder.Services.AddArchonAISales(builder.Configuration);
builder.Services.AddArchonAIMarketing(builder.Configuration);
builder.Services.AddArchonAISupport(builder.Configuration);
builder.Services.AddArchonAIAdmin(builder.Configuration);
builder.Services.AddArchonAIControlPlane(builder.Configuration);
builder.Services.AddArchonAIWorkflowDesigner();
builder.Services.AddArchonAIAgentRegistry();
builder.Services.AddArchonAIStrategyLibrary();
builder.Services.AddArchonAIWorkflowSimulation(builder.Configuration);
builder.Services.AddArchonAIMemory(builder.Configuration);
builder.Services.AddArchonAIPerception();
builder.Services.AddArchonAIOrganizationState();
builder.Services.AddSingleton<IDecisionService, DecisionService>();
builder.Services.AddSingleton<IFinancialConsequenceService, FinancialConsequenceService>();
builder.Services.AddSingleton<IHeroWorkflowService, HeroWorkflowService>();
builder.Services.AddSingleton<IPolicySimulationService, PolicySimulationService>();
builder.Services.AddSingleton<IProofAnalyticsService, ProofAnalyticsService>();
builder.Services.AddSingleton<IActionSafetyService, ActionSafetyService>();
builder.Services.AddSingleton<IInspectionService, InspectionService>();
builder.Services.AddSingleton<InspectionService>();

// ── HTTP client factory (used by OIDC token exchange) ─────────────────────────
builder.Services.AddHttpClient();

// ── Database Migrations (DbUp) ────────────────────────────────────────────────
builder.Services.AddArchonAIMigrations();

// ── Durable PostgreSQL Persistence (replaces ConcurrentDictionary stores when configured) ──
builder.Services.AddArchonAIPersistence();

// ── Health Checks ─────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck<ArchonAI.Common.Observability.EventBusHealthCheck>("event_bus", tags: ["live", "ready"])
    .AddCheck<ArchonAI.Common.Observability.TaskQueueHealthCheck>("task_queue", tags: ["live", "ready"])
    .AddCheck<ArchonAI.Common.Observability.ConnectorHealthCheck>("connectors", tags: ["ready"])
    .AddCheck<ArchonAI.Common.Observability.ModelProviderHealthCheck>("model_providers", tags: ["ready"])
    .AddCheck<ArchonAI.Common.Observability.StartupReadinessCheck>("startup", tags: ["ready"])
    .AddCheck<MigrationHealthCheck>("database_migrations", tags: ["ready"]);

var app = builder.Build();

// ── Run database migrations before accepting traffic ──────────────────────────
{
    var migrationOptions = app.Services.GetService<IOptions<MigrationOptions>>()?.Value;
    if (!string.IsNullOrWhiteSpace(migrationOptions?.ConnectionString))
    {
        var migrationRunner = app.Services.GetRequiredService<MigrationRunner>();
        var migrationResult = migrationRunner.Run();
        if (!migrationResult.Success)
        {
            app.Logger.LogCritical(
                "Database migration failed on {Script}: {Error}. Shutting down.",
                migrationResult.FailedScript, migrationResult.Error);
            return;
        }
    }
}

app.UseSerilogRequestLogging();

// Request correlation — structured logging enrichment with tenant/user/workflow context
app.UseMiddleware<RequestCorrelationMiddleware>();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

// ── Health probe endpoints (unauthenticated, K8s-compatible) ──
app.MapHealthChecks("/healthz/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = ArchonAI.Api.Security.HealthCheckResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = ArchonAI.Api.Security.HealthCheckResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = ArchonAI.Api.Security.HealthCheckResponseWriter.WriteAsync
}).AllowAnonymous();

// WebSocket hub for real-time dashboard updates
app.MapHub<ControlPlaneDashboardHub>("/hubs/control-plane-dashboard");

// ── Auth endpoints (registered directly on app) ───────────────
app.MapAuthEndpoints();
app.MapMfaEndpoints();
app.MapOidcEndpoints();

// ── API v1 endpoints ──────────────────────────────────────────
var v1 = app.MapGroup("/api/v1")
    .RequireAuthorization()
    .RequireRateLimiting("api")
    .WithTags("v1");

v1.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    version = "v1",
    authentication = "jwt",
    authorization = "rbac",
    observability = "serilog+opentelemetry+prometheus",
    scalability = "sharded-execution-queue+plugin-enabled-agents"
})).RequireAuthorization("OperatorOrAdmin");

v1.MapRegistryEndpoints();
v1.MapConnectorEndpoints();
v1.MapAgentEndpoints();
v1.MapWorkflowEndpoints();
v1.MapAdminEndpoints();
v1.MapGovernanceEndpoints();
v1.MapDecisionEndpoints();
v1.MapInfraEndpoints();
v1.MapIntelligenceEndpoints();
v1.MapStrategyEndpoints();
v1.MapOperatorEndpoints();
v1.MapTrustVisibilityEndpoints();

// ── API v2 endpoints ──────────────────────────────────────────
var v2 = app.MapGroup("/api/v2")
    .RequireAuthorization()
    .RequireRateLimiting("api")
    .WithTags("v2");

v2.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    version = "v2",
    compatibility = "backward-compatible"
})).RequireAuthorization("OperatorOrAdmin");

app.MapPrometheusScrapingEndpoint("/metrics")
    .RequireAuthorization("AdminOnly")
    .RequireRateLimiting("api");

app.Run();
