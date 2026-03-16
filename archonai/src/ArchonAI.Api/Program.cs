using Serilog;
using ArchonAI.Agents;
using ArchonAI.Api.Security;
using ArchonAI.Connectors;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Infrastructure;
using ArchonAI.Plugins;
using ArchonAI.Registry;
using ArchonAI.Runtime;

var builder = WebApplication.CreateBuilder(args)
    .AddArchonAIObservability();

builder.Services.AddArchonAISecurity(builder.Configuration);
builder.Services.AddArchonAIInfrastructure();
builder.Services.AddArchonAIConnectors(builder.Configuration);
builder.Services.AddArchonAIAgentTooling();
builder.Services.AddArchonAIPlugins(builder.Configuration);
builder.Services.AddArchonAIRegistry();
builder.Services.AddArchonAIRuntime();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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

var registry = v1.MapGroup("/registry")
    .RequireAuthorization("OperatorOrAdmin");

registry.MapGet("/agents", async (IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
{
    var agents = await capabilityRegistry.GetAllAsync(ct);
    return Results.Ok(agents);
});

registry.MapGet("/agents/{agentId:guid}", async (Guid agentId, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
{
    var agent = await capabilityRegistry.GetAgentAsync(agentId, ct);
    return agent is null ? Results.NotFound() : Results.Ok(agent);
});

registry.MapGet("/capabilities/{capability}", async (string capability, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
{
    var matches = await capabilityRegistry.QueryByCapabilityAsync(capability, ct);
    return Results.Ok(matches);
});

var traces = v1.MapGroup("/traces")
    .RequireAuthorization("OperatorOrAdmin");

traces.MapGet("", async (
    string? scope,
    string? category,
    int? limit,
    ITraceStore traceStore,
    CancellationToken ct) =>
{
    var entries = await traceStore.QueryAsync(scope, category, limit ?? 200, ct);
    return Results.Ok(entries);
});

var patterns = v1.MapGroup("/patterns")
    .RequireAuthorization("OperatorOrAdmin");

patterns.MapPost("/objectives/{objectiveId:guid}/analyze", async (
    Guid objectiveId,
    IPatternAnalyzer patternAnalyzer,
    CancellationToken ct) =>
{
    var discovered = await patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, ct);
    return Results.Ok(discovered);
});

var strategies = v1.MapGroup("/strategies")
    .RequireAuthorization("OperatorOrAdmin");

strategies.MapGet("/{objectiveType}", async (
    string objectiveType,
    IStrategyStore strategyStore,
    CancellationToken ct) =>
{
    var result = await strategyStore.QueryByObjectiveTypeAsync(objectiveType, ct);
    return Results.Ok(result);
});

strategies.MapPost("", async (
    OperationalStrategy strategy,
    IStrategyStore strategyStore,
    CancellationToken ct) =>
{
    await strategyStore.SaveAsync(strategy, ct);
    return Results.Accepted();
});

var salesforce = v1.MapGroup("/connectors/salesforce")
    .RequireAuthorization("OperatorOrAdmin");

salesforce.MapGet("/status", (ISalesforceConnector sfConnector) =>
{
    var status = sfConnector.GetStatus();
    return Results.Ok(status);
});

salesforce.MapPost("/authenticate", async (ISalesforceConnector sfConnector, CancellationToken ct) =>
{
    var result = await sfConnector.AuthenticateAsync(ct);
    return result.IsAuthenticated ? Results.Ok(result) : Results.Problem(result.Error ?? "Authentication failed", statusCode: 401);
});

salesforce.MapGet("/accounts", async (string? filter, ISalesforceConnector sfConnector, CancellationToken ct) =>
{
    var records = await sfConnector.QueryAccountsAsync(filter ?? string.Empty, ct);
    return Results.Ok(records);
});

salesforce.MapGet("/contacts", async (string? filter, ISalesforceConnector sfConnector, CancellationToken ct) =>
{
    var records = await sfConnector.QueryContactsAsync(filter ?? string.Empty, ct);
    return Results.Ok(records);
});

salesforce.MapGet("/opportunities", async (string? filter, ISalesforceConnector sfConnector, CancellationToken ct) =>
{
    var records = await sfConnector.QueryOpportunitiesAsync(filter ?? string.Empty, ct);
    return Results.Ok(records);
});

salesforce.MapPost("/records/{objectType}", async (string objectType, Dictionary<string, string> fields, ISalesforceConnector sfConnector, CancellationToken ct) =>
{
    string recordId = await sfConnector.CreateRecordAsync(objectType, fields, ct);
    return Results.Created($"/api/v1/connectors/salesforce/records/{objectType}/{recordId}", new { recordId });
});

salesforce.MapPatch("/records/{objectType}/{recordId}", async (string objectType, string recordId, Dictionary<string, string> fields, ISalesforceConnector sfConnector, CancellationToken ct) =>
{
    await sfConnector.UpdateRecordAsync(objectType, recordId, fields, ct);
    return Results.Ok(new { recordId, updated = true });
});

var hubspot = v1.MapGroup("/connectors/hubspot")
    .RequireAuthorization("OperatorOrAdmin");

hubspot.MapGet("/status", (IHubSpotConnector hsConnector) =>
{
    var status = hsConnector.GetStatus();
    return Results.Ok(status);
});

hubspot.MapPost("/authenticate", async (IHubSpotConnector hsConnector, CancellationToken ct) =>
{
    var result = await hsConnector.AuthenticateAsync(ct);
    return result.IsAuthenticated ? Results.Ok(result) : Results.Problem(result.Error ?? "Authentication failed", statusCode: 401);
});

hubspot.MapGet("/contacts", async (string? filter, int? limit, IHubSpotConnector hsConnector, CancellationToken ct) =>
{
    var records = await hsConnector.GetContactsAsync(filter, limit ?? 100, ct);
    return Results.Ok(records);
});

hubspot.MapGet("/deals", async (string? filter, int? limit, IHubSpotConnector hsConnector, CancellationToken ct) =>
{
    var records = await hsConnector.GetDealsAsync(filter, limit ?? 100, ct);
    return Results.Ok(records);
});

hubspot.MapPost("/records/{objectType}", async (string objectType, Dictionary<string, string> properties, IHubSpotConnector hsConnector, CancellationToken ct) =>
{
    string recordId = await hsConnector.CreateRecordAsync(objectType, properties, ct);
    return Results.Created($"/api/v1/connectors/hubspot/records/{objectType}/{recordId}", new { recordId });
});

hubspot.MapPatch("/records/{objectType}/{recordId}", async (string objectType, string recordId, Dictionary<string, string> properties, IHubSpotConnector hsConnector, CancellationToken ct) =>
{
    await hsConnector.UpdatePipelineRecordAsync(objectType, recordId, properties, ct);
    return Results.Ok(new { recordId, updated = true });
});

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
