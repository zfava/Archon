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

// ── Environment posture — gates in-memory fallbacks in production-like environments ──
{
    bool isProdLike = builder.Environment.IsProduction()
        || string.Equals(builder.Environment.EnvironmentName, "Staging", StringComparison.OrdinalIgnoreCase);
    builder.Services.AddSingleton(new ArchonAI.Common.EnvironmentPosture { IsProductionLike = isProdLike });
}

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
builder.Services.AddHostedService<GovernanceEventSubscriber>();

// ── Demo configuration ──────────────────────────────────────────────────────
builder.Services.Configure<ArchonAI.Api.Endpoints.DemoOptions>(builder.Configuration.GetSection("Demo"));

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
    .AddCheck<ArchonAI.Common.Observability.IdentityPersistenceHealthCheck>("identity_persistence", tags: ["ready"])
    .AddCheck<MigrationHealthCheck>("database_migrations", tags: ["ready"])
    .AddCheck<ArchonAI.Common.Observability.ProductionConfigHealthCheck>("production_config", tags: ["ready"])
    .AddCheck<ArchonAI.Common.Observability.FallbackPostureHealthCheck>("fallback_posture", tags: ["ready"]);

var app = builder.Build();

// ── Production configuration validation ──────────────────────────────────────
{
    var env = app.Environment;
    var persistenceOpts = app.Services.GetService<IOptions<PersistenceOptions>>()?.Value;
    bool hasDurablePersistence = !string.IsNullOrWhiteSpace(persistenceOpts?.ConnectionString);
    bool isProductionLike = env.IsProduction()
        || string.Equals(env.EnvironmentName, "Staging", StringComparison.OrdinalIgnoreCase);

    // Configure the identity persistence health check
    ArchonAI.Common.Observability.IdentityPersistenceHealthCheck.Configure(isProductionLike, hasDurablePersistence);

    // Build a snapshot of all configuration relevant to production validation
    var jwtSection = app.Configuration.GetSection("Security:Jwt");
    var modelSection = app.Configuration.GetSection("ModelProviders");
    var oidcSection = app.Configuration.GetSection("Oidc");

    // Subsystem persistence configuration
    var memorySection = app.Configuration.GetSection("MemoryPersistence");
    var knowledgeSection = app.Configuration.GetSection("KnowledgeGraph");
    var telemetrySection = app.Configuration.GetSection("TelemetryPersistence");
    var eventBusSection = app.Configuration.GetSection("EventBus");

    var configSnapshot = new ArchonAI.Common.Observability.ConfigSnapshot
    {
        EnvironmentName = env.EnvironmentName,
        IsProductionLike = isProductionLike,

        JwtSigningKey = jwtSection["SigningKey"]
            ?? Environment.GetEnvironmentVariable("ARCHONAI_JWT_SIGNING_KEY"),
        TotpEncryptionKey = Environment.GetEnvironmentVariable("ARCHONAI_TOTP_ENCRYPTION_KEY"),
        PersistenceConnectionString = persistenceOpts?.ConnectionString,

        // Subsystem persistence
        MemoryPersistenceConnectionString = memorySection["ConnectionString"],
        KnowledgeGraphConnectionString = knowledgeSection["ConnectionString"],
        TelemetryConnectionString = telemetrySection["ConnectionString"],
        EventBusUseNats = bool.TryParse(eventBusSection["UseNats"], out var natsE) && natsE,

        OpenAiEnabled = bool.TryParse(modelSection["OpenAI:Enabled"], out var oaiE) ? oaiE : true,
        OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? modelSection["OpenAI:ApiKey"],
        AnthropicEnabled = bool.TryParse(modelSection["Anthropic:Enabled"], out var antE) ? antE : true,
        AnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? modelSection["Anthropic:ApiKey"],
        AzureOpenAiEnabled = bool.TryParse(modelSection["AzureOpenAI:Enabled"], out var azE) && azE,
        AzureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? modelSection["AzureOpenAI:ApiKey"],
        AzureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? modelSection["AzureOpenAI:Endpoint"],

        OidcCallbackBaseUrl = oidcSection["CallbackBaseUrl"],

        Connectors = BuildConnectorStates(app.Configuration),
    };

    // Set fallback posture health check environment
    ArchonAI.Common.Observability.FallbackPostureHealthCheck.SetEnvironment(isProductionLike);

    var validationResult = ArchonAI.Common.Observability.ProductionConfigValidator.Validate(configSnapshot);
    ArchonAI.Common.Observability.ProductionConfigHealthCheck.SetResult(validationResult);
    ArchonAI.Common.Observability.ProductionConfigValidator.LogResult(validationResult, app.Logger);

    // In production mode, block startup if critical misconfigurations exist
    if (validationResult.IsProductionMode && validationResult.HasCriticalFindings)
    {
        app.Logger.LogCritical(
            "FATAL: Production startup blocked due to {Count} critical configuration error(s). " +
            "Resolve the issues above and restart. The readiness endpoint will report unhealthy.",
            validationResult.Findings.Count(f =>
                f.Severity == ArchonAI.Common.Observability.ConfigSeverity.Critical));
        return;
    }

    // Fallback posture summary — record subsystem durability for health check reporting
    ArchonAI.Common.Observability.FallbackPostureHealthCheck.RecordPosture(
        "Persistence", hasDurablePersistence ? "PostgreSQL" : "InMemory", hasDurablePersistence);
    ArchonAI.Common.Observability.FallbackPostureHealthCheck.RecordPosture(
        "EventBus",
        bool.TryParse(eventBusSection["UseNats"], out var ebNats) && ebNats ? "NATS" : "InMemory",
        bool.TryParse(eventBusSection["UseNats"], out var ebNats2) && ebNats2);
    ArchonAI.Common.Observability.FallbackPostureHealthCheck.RecordPosture(
        "MemoryStore",
        !string.IsNullOrWhiteSpace(memorySection["ConnectionString"]) ? "PostgreSQL" : "InMemory",
        !string.IsNullOrWhiteSpace(memorySection["ConnectionString"]));
    ArchonAI.Common.Observability.FallbackPostureHealthCheck.RecordPosture(
        "KnowledgeGraph",
        !string.IsNullOrWhiteSpace(knowledgeSection["ConnectionString"]) ? "PostgreSQL" : "InMemory",
        !string.IsNullOrWhiteSpace(knowledgeSection["ConnectionString"]));
    ArchonAI.Common.Observability.FallbackPostureHealthCheck.RecordPosture(
        "Telemetry",
        !string.IsNullOrWhiteSpace(telemetrySection["ConnectionString"]) ? "PostgreSQL" : "InMemory",
        !string.IsNullOrWhiteSpace(telemetrySection["ConnectionString"]));
}

static Dictionary<string, ArchonAI.Common.Observability.ConnectorConfigState> BuildConnectorStates(
    IConfiguration configuration)
{
    var states = new Dictionary<string, ArchonAI.Common.Observability.ConnectorConfigState>();
    var connectorNames = new[] { "Salesforce", "HubSpot", "QuickBooks", "Slack", "GoogleWorkspace", "Microsoft365" };

    foreach (var name in connectorNames)
    {
        var section = configuration.GetSection($"Connectors:{name}");
        if (!section.Exists()) continue;

        states[name] = new ArchonAI.Common.Observability.ConnectorConfigState
        {
            Enabled = !bool.TryParse(section["Enabled"], out var disabled) || !disabled,
            HasClientId = !string.IsNullOrWhiteSpace(section["ClientId"]),
            HasClientSecret = !string.IsNullOrWhiteSpace(section["ClientSecret"]),
            HasEndpoint = !string.IsNullOrWhiteSpace(section["LoginUrl"] ?? section["BaseUrl"]),
        };
    }

    return states;
}

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
v1.MapDemoEndpoints();

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
