using System.Text.Json;
using Serilog;
using ArchonAI.Agents;
using ArchonAI.Api.Security;
using ArchonAI.Connectors;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AuditLog;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.OperationalTwin;
using ArchonAI.Core.Models.Scenario;
using ArchonAI.Core.Models.ExceptionIntelligence;
using ArchonAI.Core.Models.ExecutiveCommand;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Rbac;
using ArchonAI.Core.Models.Monitoring;
using ArchonAI.Core.Models.Workflow;
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
using ArchonAI.Core.Models.ControlPlane;
using ArchonAI.Runtime;
using ArchonAI.WorkflowRuntime;
using ArchonAI.WorkflowDesigner;
using ArchonAI.AgentRegistry;
using ArchonAI.Core.Models.AgentRegistry;
using ArchonAI.StrategyLibrary;
using ArchonAI.Core.Models.StrategyLibrary;
using ArchonAI.WorkflowSimulation;
using ArchonAI.Core.Models.Telemetry;
using ArchonAI.Core.Models.Cluster;
using ArchonAI.ControlPlane.Hubs;
using ArchonAI.Memory;
using ArchonAI.Perception;
using ArchonAI.Core.Models.Perception;
using ArchonAI.OrganizationState;
using ArchonAI.Core.Models.OrganizationState;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Memory;
using ArchonAI.Core.Models.Models.Routing;
using ArchonAI.Core.Models.Reasoning;
using ArchonAI.ModelRouter;
using ArchonAI.Core.Models.Simulation;
using ArchonAI.Core.Models.HumanOverride;
using ArchonAI.Core.Models.Explanation;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.PolicySimulation;
using ArchonAI.Core.Models.ProofAnalytics;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.Inspection;
using ArchonAI.Identity;
using ArchonAI.Persistence;

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

// ── Durable PostgreSQL Persistence (replaces ConcurrentDictionary stores when configured) ──
builder.Services.AddArchonAIPersistence();

// ── Health Checks ─────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck<ArchonAI.Common.Observability.EventBusHealthCheck>("event_bus", tags: ["live", "ready"])
    .AddCheck<ArchonAI.Common.Observability.TaskQueueHealthCheck>("task_queue", tags: ["live", "ready"])
    .AddCheck<ArchonAI.Common.Observability.ConnectorHealthCheck>("connectors", tags: ["ready"])
    .AddCheck<ArchonAI.Common.Observability.ModelProviderHealthCheck>("model_providers", tags: ["ready"])
    .AddCheck<ArchonAI.Common.Observability.StartupReadinessCheck>("startup", tags: ["ready"]);

var app = builder.Build();

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

// ── Auth endpoints (unauthenticated) ─────────────────────────
var auth = app.MapGroup("/api/auth")
    .AllowAnonymous()
    .RequireRateLimiting("api")
    .WithTags("auth");

auth.MapPost("/register", async (
    AuthenticationService authService,
    RegisterRequest req,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password)
        || string.IsNullOrWhiteSpace(req.OrganizationName) || string.IsNullOrWhiteSpace(req.DisplayName))
        return Results.BadRequest(new { error = "All fields are required." });

    if (req.Password.Length < 8)
        return Results.BadRequest(new { error = "Password must be at least 8 characters." });

    var result = await authService.RegisterAsync(req.OrganizationName, req.Email, req.Password, req.DisplayName, ct);
    if (result is null)
        return Results.Conflict(new { error = "Email already registered." });

    var (org, user, tokens) = result.Value;
    return Results.Ok(new AuthResponse(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc,
        new UserInfo(user.Id, user.Email, user.DisplayName, user.Role),
        new OrgInfo(org.Id, org.Name, org.Slug)));
});

auth.MapPost("/login", async (
    AuthenticationService authService,
    LoginRequest req,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    var result = await authService.LoginAsync(req.Email, req.Password, ct);
    if (result is null)
        return Results.Unauthorized();

    var (tokens, user, org) = result.Value;
    return Results.Ok(new AuthResponse(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc,
        new UserInfo(user.Id, user.Email, user.DisplayName, user.Role),
        new OrgInfo(org.Id, org.Name, org.Slug)));
});

auth.MapPost("/refresh", async (
    AuthenticationService authService,
    RefreshRequest req,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.RefreshToken))
        return Results.BadRequest(new { error = "Refresh token is required." });

    var tokens = await authService.RefreshAsync(req.RefreshToken, ct);
    if (tokens is null)
        return Results.Unauthorized();

    return Results.Ok(new { accessToken = tokens.AccessToken, refreshToken = tokens.RefreshToken, expiresAtUtc = tokens.ExpiresAtUtc });
});

auth.MapPost("/logout", async (
    AuthenticationService authService,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var sub = ctx.User?.FindFirst("sub")?.Value;
    if (sub is not null && Guid.TryParse(sub, out var userId))
        await authService.LogoutAsync(userId, ct);
    return Results.Ok(new { message = "Logged out." });
}).RequireAuthorization();

auth.MapGet("/me", async (
    HttpContext ctx,
    IUserStore userStore,
    IOrganizationStore orgStore,
    IMembershipStore membershipStore,
    CancellationToken ct) =>
{
    var sub = ctx.User?.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var userId))
        return Results.Unauthorized();

    var user = await userStore.GetByIdAsync(userId, ct);
    if (user is null) return Results.Unauthorized();

    var org = await orgStore.GetByIdAsync(user.OrganizationId, ct);
    if (org is null) return Results.Unauthorized();

    var membership = await membershipStore.GetAsync(user.Id, org.Id, ct);
    string role = membership?.Role ?? user.Role;

    return Results.Ok(new MeResponse(
        new UserInfo(user.Id, user.Email, user.DisplayName, role),
        new OrgInfo(org.Id, org.Name, org.Slug)));
}).RequireAuthorization();

auth.MapPost("/invite", async (
    AuthenticationService authService,
    HttpContext ctx,
    InviteRequest req,
    CancellationToken ct) =>
{
    var orgIdClaim = ctx.User?.FindFirst("org_id")?.Value;
    var subClaim = ctx.User?.FindFirst("sub")?.Value;
    if (orgIdClaim is null || !Guid.TryParse(orgIdClaim, out var orgId)
        || subClaim is null || !Guid.TryParse(subClaim, out var userId))
        return Results.Unauthorized();

    var result = await authService.InviteUserAsync(orgId, req.Email, req.Role ?? "Operator", userId, ct);
    if (result is null)
        return Results.NotFound(new { error = "Organization not found." });

    return Results.Ok(new { inviteToken = result.Value.RawToken, expiresAtUtc = result.Value.Invite.ExpiresAtUtc });
}).RequireAuthorization("AdminOnly");

auth.MapPost("/accept-invite", async (
    AuthenticationService authService,
    AcceptInviteRequest req,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.InviteToken) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Invite token and password are required." });

    var result = await authService.AcceptInviteAsync(req.InviteToken, req.Password, req.DisplayName ?? "User", ct);
    if (result is null)
        return Results.BadRequest(new { error = "Invalid or expired invite." });

    return Results.Ok(new { accessToken = result.Value.Tokens.AccessToken, refreshToken = result.Value.Tokens.RefreshToken });
});

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

registry.MapGet("/task-types/{taskType}", async (string taskType, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
{
    var matches = await capabilityRegistry.QueryByTaskTypeAsync(taskType, ct);
    return Results.Ok(matches);
});

registry.MapGet("/select-best", async (string capability, string? taskType, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
{
    var selection = await capabilityRegistry.SelectBestAgentAsync(capability, taskType, ct);
    return selection is null
        ? Results.NotFound(new { error = $"No agent found for capability '{capability}'." })
        : Results.Ok(selection);
});

registry.MapGet("/agents/{agentId:guid}/performance", async (Guid agentId, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
{
    var snapshot = await capabilityRegistry.GetPerformanceSnapshotAsync(agentId, ct);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
});

registry.MapPost("/agents/{agentId:guid}/task-types", async (Guid agentId, RegisterTaskTypesRequest req, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
{
    await capabilityRegistry.RegisterSupportedTaskTypesAsync(agentId, req.TaskTypes, ct);
    var updated = await capabilityRegistry.GetAgentAsync(agentId, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).RequireAuthorization("AdminOnly");

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

var quickbooks = v1.MapGroup("/connectors/quickbooks")
    .RequireAuthorization("OperatorOrAdmin");

quickbooks.MapGet("/status", (IQuickBooksConnector qbConnector) =>
{
    var status = qbConnector.GetStatus();
    return Results.Ok(status);
});

quickbooks.MapPost("/authenticate", async (IQuickBooksConnector qbConnector, CancellationToken ct) =>
{
    var result = await qbConnector.AuthenticateAsync(ct);
    return result.IsAuthenticated ? Results.Ok(result) : Results.Problem(result.Error ?? "Authentication failed", statusCode: 401);
});

quickbooks.MapGet("/reports", async (string? reportType, string? startDate, string? endDate, IQuickBooksConnector qbConnector, CancellationToken ct) =>
{
    var records = await qbConnector.GetFinancialReportsAsync(reportType ?? "ProfitAndLoss", startDate, endDate, ct);
    return Results.Ok(records);
});

quickbooks.MapPost("/invoices", async (QuickBooksInvoiceRequest invoiceRequest, IQuickBooksConnector qbConnector, CancellationToken ct) =>
{
    string invoiceId = await qbConnector.CreateInvoiceAsync(invoiceRequest.CustomerId, invoiceRequest.LineItems, ct);
    return Results.Created($"/api/v1/connectors/quickbooks/invoices/{invoiceId}", new { invoiceId });
});

quickbooks.MapPatch("/customers/{customerId}", async (string customerId, Dictionary<string, string> fields, IQuickBooksConnector qbConnector, CancellationToken ct) =>
{
    await qbConnector.UpdateCustomerAsync(customerId, fields, ct);
    return Results.Ok(new { customerId, updated = true });
});

quickbooks.MapGet("/transactions", async (string? accountId, string? startDate, string? endDate, int? limit, IQuickBooksConnector qbConnector, CancellationToken ct) =>
{
    var records = await qbConnector.GetTransactionHistoryAsync(accountId, startDate, endDate, limit ?? 100, ct);
    return Results.Ok(records);
});

var slack = v1.MapGroup("/connectors/slack")
    .RequireAuthorization("OperatorOrAdmin");

slack.MapGet("/status", (ISlackConnector slackConnector) =>
{
    var status = slackConnector.GetStatus();
    return Results.Ok(status);
});

slack.MapPost("/authenticate", async (ISlackConnector slackConnector, CancellationToken ct) =>
{
    var result = await slackConnector.AuthenticateAsync(ct);
    return result.IsAuthenticated ? Results.Ok(result) : Results.Problem(result.Error ?? "Authentication failed", statusCode: 401);
});

slack.MapPost("/messages", async (SlackSendMessageRequest msgRequest, ISlackConnector slackConnector, CancellationToken ct) =>
{
    var result = await slackConnector.SendMessageAsync(msgRequest.Channel, msgRequest.Text, msgRequest.ThreadTs, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.Problem(result.Error ?? "Send failed", statusCode: 400);
});

slack.MapGet("/channels", async (int? limit, ISlackConnector slackConnector, CancellationToken ct) =>
{
    var channels = await slackConnector.GetChannelsAsync(limit ?? 100, ct);
    return Results.Ok(channels);
});

slack.MapGet("/channels/{channel}/history", async (string channel, int? limit, string? oldest, string? latest, ISlackConnector slackConnector, CancellationToken ct) =>
{
    var messages = await slackConnector.ReadChannelHistoryAsync(channel, limit ?? 50, oldest, latest, ct);
    return Results.Ok(messages);
});

slack.MapPost("/alerts", async (SlackAlertRequest alertRequest, ISlackConnector slackConnector, CancellationToken ct) =>
{
    var result = await slackConnector.PostAlertAsync(alertRequest.Channel, alertRequest.AlertLevel, alertRequest.Title, alertRequest.Details, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.Problem(result.Error ?? "Alert failed", statusCode: 400);
});

slack.MapPost("/webhooks/events", async (HttpContext context, ISlackConnector slackConnector, CancellationToken ct) =>
{
    string body = await new StreamReader(context.Request.Body).ReadToEndAsync(ct);
    string signature = context.Request.Headers["X-Slack-Signature"].FirstOrDefault() ?? string.Empty;
    string timestamp = context.Request.Headers["X-Slack-Request-Timestamp"].FirstOrDefault() ?? string.Empty;

    bool processed = await slackConnector.ProcessWebhookEventAsync(body, signature, timestamp, ct);
    return processed ? Results.Ok(new { ok = true }) : Results.Problem("Webhook verification failed", statusCode: 401);
}).AllowAnonymous();

var gws = v1.MapGroup("/connectors/google-workspace")
    .RequireAuthorization("OperatorOrAdmin");

gws.MapGet("/status", (IGoogleWorkspaceConnector gwsConnector) =>
{
    var status = gwsConnector.GetStatus();
    return Results.Ok(status);
});

gws.MapPost("/authenticate", async (IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    var result = await gwsConnector.AuthenticateAsync(ct);
    return result.IsAuthenticated ? Results.Ok(result) : Results.Problem(result.Error ?? "Authentication failed", statusCode: 401);
});

// Gmail endpoints
gws.MapGet("/gmail/messages", async (string? query, int? maxResults, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    var messages = await gwsConnector.GetEmailsAsync(query, maxResults ?? 20, ct);
    return Results.Ok(messages);
});

gws.MapPost("/gmail/send", async (GmailSendRequest emailRequest, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    var result = await gwsConnector.SendEmailAsync(emailRequest.To, emailRequest.Subject, emailRequest.Body, emailRequest.IsHtml, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.Problem(result.Error ?? "Send failed", statusCode: 400);
});

// Google Docs endpoints
gws.MapGet("/docs/{documentId}", async (string documentId, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    var doc = await gwsConnector.GetDocumentAsync(documentId, ct);
    return Results.Ok(doc);
});

gws.MapPost("/docs", async (GoogleDocCreateRequest docRequest, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    string documentId = await gwsConnector.CreateDocumentAsync(docRequest.Title, docRequest.Content, ct);
    return Results.Created($"/api/v1/connectors/google-workspace/docs/{documentId}", new { documentId });
});

// Google Sheets endpoints
gws.MapGet("/sheets/{spreadsheetId}", async (string spreadsheetId, string? range, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    var data = await gwsConnector.ReadSpreadsheetAsync(spreadsheetId, range ?? "Sheet1!A1:Z1000", ct);
    return Results.Ok(data);
});

gws.MapPut("/sheets/{spreadsheetId}", async (string spreadsheetId, GoogleSheetWriteRequest writeRequest, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    int updatedCells = await gwsConnector.WriteSpreadsheetAsync(spreadsheetId, writeRequest.Range, writeRequest.Values, ct);
    return Results.Ok(new { spreadsheetId, updatedCells });
});

// Google Drive endpoints
gws.MapGet("/drive/files", async (string? query, int? maxResults, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    var files = await gwsConnector.ListFilesAsync(query, maxResults ?? 50, ct);
    return Results.Ok(files);
});

gws.MapGet("/drive/files/{fileId}", async (string fileId, IGoogleWorkspaceConnector gwsConnector, CancellationToken ct) =>
{
    var file = await gwsConnector.GetFileMetadataAsync(fileId, ct);
    return Results.Ok(file);
});

var m365 = v1.MapGroup("/connectors/microsoft365")
    .RequireAuthorization("OperatorOrAdmin");

m365.MapGet("/status", (IMicrosoft365Connector m365Connector) =>
{
    var status = m365Connector.GetStatus();
    return Results.Ok(status);
});

m365.MapPost("/authenticate", async (IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var result = await m365Connector.AuthenticateAsync(ct);
    return result.IsAuthenticated ? Results.Ok(result) : Results.Problem(result.Error ?? "Authentication failed", statusCode: 401);
});

// Outlook endpoints
m365.MapGet("/outlook/messages", async (string? filter, int? top, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var messages = await m365Connector.GetEmailsAsync(filter, top ?? 20, ct);
    return Results.Ok(messages);
});

m365.MapPost("/outlook/send", async (M365SendEmailRequest emailRequest, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var result = await m365Connector.SendEmailAsync(emailRequest.To, emailRequest.Subject, emailRequest.Body, emailRequest.IsHtml, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.Problem(result.Error ?? "Send failed", statusCode: 400);
});

// Teams endpoints
m365.MapGet("/teams/{teamId}/channels", async (string teamId, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var channels = await m365Connector.GetTeamsChannelsAsync(teamId, ct);
    return Results.Ok(channels);
});

m365.MapPost("/teams/{teamId}/channels/{channelId}/messages", async (string teamId, string channelId, M365TeamsMessageRequest msgRequest, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var result = await m365Connector.SendTeamsMessageAsync(teamId, channelId, msgRequest.Content, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.Problem(result.Error ?? "Send failed", statusCode: 400);
});

m365.MapGet("/teams/{teamId}/channels/{channelId}/messages", async (string teamId, string channelId, int? top, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var messages = await m365Connector.GetTeamsMessagesAsync(teamId, channelId, top ?? 20, ct);
    return Results.Ok(messages);
});

// SharePoint endpoints
m365.MapGet("/sharepoint/sites/{siteId}/items", async (string siteId, string? listId, int? top, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var items = await m365Connector.GetSharePointItemsAsync(siteId, listId, top ?? 50, ct);
    return Results.Ok(items);
});

m365.MapGet("/sharepoint/sites/{siteId}/drives/{driveId}/items/{itemId}", async (string siteId, string driveId, string itemId, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var item = await m365Connector.GetSharePointItemAsync(siteId, driveId, itemId, ct);
    return Results.Ok(item);
});

// OneDrive endpoints
m365.MapGet("/onedrive/files", async (string? folderId, int? top, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var files = await m365Connector.ListOneDriveFilesAsync(folderId, top ?? 50, ct);
    return Results.Ok(files);
});

m365.MapGet("/onedrive/files/{itemId}", async (string itemId, IMicrosoft365Connector m365Connector, CancellationToken ct) =>
{
    var item = await m365Connector.GetOneDriveItemAsync(itemId, ct);
    return Results.Ok(item);
});

var ops = v1.MapGroup("/operations")
    .RequireAuthorization("OperatorOrAdmin");

ops.MapGet("/status", (IOperationsEngine opsEngine) =>
{
    var status = opsEngine.GetStatus();
    return Results.Ok(status);
});

ops.MapPost("/workflows/{objectiveId:guid}/analyze", async (Guid objectiveId, Dictionary<string, string>? parameters, IOperationsEngine opsEngine, CancellationToken ct) =>
{
    var result = await opsEngine.AnalyzeWorkflowAsync(objectiveId, parameters ?? new Dictionary<string, string>(), ct);
    return Results.Ok(result);
});

ops.MapGet("/inefficiencies", async (string? scope, int? maxResults, IOperationsEngine opsEngine, CancellationToken ct) =>
{
    var insights = await opsEngine.IdentifyInefficienciesAsync(scope ?? "*", maxResults ?? 20, ct);
    return Results.Ok(insights);
});

ops.MapPost("/workflows/{objectiveId:guid}/recommendations", async (Guid objectiveId, IOperationsEngine opsEngine, CancellationToken ct) =>
{
    var recommendations = await opsEngine.RecommendImprovementsAsync(objectiveId, ct);
    return Results.Ok(recommendations);
});

ops.MapPost("/workflows/coordinate", async (WorkflowCoordinationRequest coordRequest, IOperationsEngine opsEngine, CancellationToken ct) =>
{
    var result = await opsEngine.CoordinateWorkflowAsync(
        coordRequest.WorkflowTemplate, coordRequest.AgentCapabilities, coordRequest.Inputs, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.Problem("Coordination failed", statusCode: 400);
});

var finance = v1.MapGroup("/finance")
    .RequireAuthorization("OperatorOrAdmin");

finance.MapGet("/status", (IFinanceEngine finEngine) =>
{
    var status = finEngine.GetStatus();
    return Results.Ok(status);
});

finance.MapPost("/analyze", async (FinanceAnalysisRequest analysisRequest, IFinanceEngine finEngine, CancellationToken ct) =>
{
    var result = await finEngine.AnalyzePerformanceAsync(analysisRequest.Scope, analysisRequest.Parameters, ct);
    return Results.Ok(result);
});

finance.MapGet("/anomalies", async (string? scope, double? sensitivity, int? maxResults, IFinanceEngine finEngine, CancellationToken ct) =>
{
    var anomalies = await finEngine.DetectAnomaliesAsync(scope ?? "*", sensitivity ?? 0.7, maxResults ?? 20, ct);
    return Results.Ok(anomalies);
});

finance.MapPost("/summary", async (FinanceSummaryRequest summaryRequest, IFinanceEngine finEngine, CancellationToken ct) =>
{
    var summary = await finEngine.GenerateSummaryAsync(summaryRequest.Scope, summaryRequest.Period, ct);
    return Results.Ok(summary);
});

finance.MapPost("/budget", async (BudgetAssistRequest budgetRequest, IFinanceEngine finEngine, CancellationToken ct) =>
{
    var result = await finEngine.AssistBudgetingAsync(budgetRequest.DepartmentId, budgetRequest.Parameters, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.Problem("Budget workflow failed", statusCode: 400);
});

// Sales endpoints
var sales = v1.MapGroup("/sales")
    .RequireAuthorization("OperatorOrAdmin");

sales.MapGet("/status", (ISalesEngine salesEngine) => Results.Ok(salesEngine.GetStatus()));

sales.MapPost("/pipelines/{pipelineId}/analyze", async (string pipelineId, Dictionary<string, string>? parameters, ISalesEngine salesEngine, CancellationToken ct) =>
{
    var result = await salesEngine.AnalyzePipelineAsync(pipelineId, parameters ?? new Dictionary<string, string>(), ct);
    return Results.Ok(result);
});

sales.MapGet("/pipelines/{pipelineId}/opportunities", async (string pipelineId, int? maxResults, ISalesEngine salesEngine, CancellationToken ct) =>
{
    var opportunities = await salesEngine.PrioritizeOpportunitiesAsync(pipelineId, maxResults ?? 20, ct);
    return Results.Ok(opportunities);
});

sales.MapGet("/opportunities/{opportunityId}/outreach", async (string opportunityId, ISalesEngine salesEngine, CancellationToken ct) =>
{
    var recommendations = await salesEngine.RecommendOutreachAsync(opportunityId, ct);
    return Results.Ok(recommendations);
});

sales.MapGet("/metrics", async (string? scope, string? period, ISalesEngine salesEngine, CancellationToken ct) =>
{
    var metrics = await salesEngine.GetMetricsAsync(scope ?? "*", period ?? "current", ct);
    return Results.Ok(metrics);
});

// Marketing endpoints
var marketing = v1.MapGroup("/marketing")
    .RequireAuthorization("OperatorOrAdmin");

marketing.MapGet("/status", (IMarketingEngine mktEngine) => Results.Ok(mktEngine.GetStatus()));

marketing.MapPost("/campaigns/{campaignId}/analyze", async (string campaignId, Dictionary<string, string>? parameters, IMarketingEngine mktEngine, CancellationToken ct) =>
{
    var result = await mktEngine.AnalyzeCampaignAsync(campaignId, parameters ?? new Dictionary<string, string>(), ct);
    return Results.Ok(result);
});

marketing.MapGet("/strategies", async (string? scope, IMarketingEngine mktEngine, CancellationToken ct) =>
{
    var strategies = await mktEngine.RecommendStrategiesAsync(scope ?? "*", ct);
    return Results.Ok(strategies);
});

marketing.MapGet("/engagement", async (string? scope, string? period, IMarketingEngine mktEngine, CancellationToken ct) =>
{
    var metrics = await mktEngine.GetEngagementMetricsAsync(scope ?? "*", period ?? "current", ct);
    return Results.Ok(metrics);
});

// Support endpoints
var support = v1.MapGroup("/support")
    .RequireAuthorization("OperatorOrAdmin");

support.MapGet("/status", (ISupportEngine supEngine) => Results.Ok(supEngine.GetStatus()));

support.MapPost("/tickets/analyze", async (SupportAnalysisRequest analysisRequest, ISupportEngine supEngine, CancellationToken ct) =>
{
    var result = await supEngine.AnalyzeTicketsAsync(analysisRequest.Scope, analysisRequest.Parameters, ct);
    return Results.Ok(result);
});

support.MapGet("/recurring-issues", async (string? scope, int? minOccurrences, ISupportEngine supEngine, CancellationToken ct) =>
{
    var issues = await supEngine.DetectRecurringIssuesAsync(scope ?? "*", minOccurrences ?? 3, ct);
    return Results.Ok(issues);
});

support.MapGet("/auto-responses/{issueCategory}", async (string issueCategory, ISupportEngine supEngine, CancellationToken ct) =>
{
    var responses = await supEngine.RecommendAutoResponsesAsync(issueCategory, ct);
    return Results.Ok(responses);
});

// Admin endpoints
var admin = v1.MapGroup("/admin")
    .RequireAuthorization("AdminOnly");

admin.MapGet("/status", (IAdminService adminService) => Results.Ok(adminService.GetStatus()));

admin.MapGet("/agents", async (IAdminService adminService, CancellationToken ct) =>
{
    var agents = await adminService.GetAgentsAsync(ct);
    return Results.Ok(agents);
});

admin.MapGet("/agents/{agentId:guid}", async (Guid agentId, IAdminService adminService, CancellationToken ct) =>
{
    var agent = await adminService.GetAgentAsync(agentId, ct);
    return agent is null ? Results.NotFound() : Results.Ok(agent);
});

admin.MapPatch("/agents/{agentId:guid}/enabled", async (Guid agentId, AgentEnabledRequest enabledRequest, IAdminService adminService, CancellationToken ct) =>
{
    await adminService.SetAgentEnabledAsync(agentId, enabledRequest.Enabled, ct);
    return Results.Ok(new { agentId, enabled = enabledRequest.Enabled });
});

admin.MapGet("/workflows", async (IAdminService adminService, CancellationToken ct) =>
{
    var workflows = await adminService.GetWorkflowsAsync(ct);
    return Results.Ok(workflows);
});

admin.MapGet("/workflows/{workflowId:guid}", async (Guid workflowId, IAdminService adminService, CancellationToken ct) =>
{
    var workflow = await adminService.GetWorkflowAsync(workflowId, ct);
    return workflow is null ? Results.NotFound() : Results.Ok(workflow);
});

admin.MapPost("/workflows/{workflowId:guid}/cancel", async (
    Guid workflowId,
    HttpContext ctx,
    IAdminService adminService,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value
        ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (tenantId is null || userId is null) return Results.Unauthorized();

    if (await gov.RequiresApprovalAsync("workflow.cancel", ct))
    {
        var payload = JsonSerializer.Serialize(new { workflowId = workflowId.ToString() });
        var gate = await gov.RequestApprovalAsync(
            "workflow.cancel", workflowId.ToString(), tenantId, userId,
            "Workflow cancellation requested", payload, ct);
        return Results.Accepted($"/api/v1/governance/{gate.Id}", gate);
    }

    await adminService.CancelWorkflowAsync(workflowId, ct);
    return Results.Ok(new { workflowId, cancelled = true });
});

admin.MapGet("/policy", async (IAdminService adminService, CancellationToken ct) =>
{
    var policy = await adminService.GetPolicyConfigAsync(ct);
    return Results.Ok(policy);
});

admin.MapPut("/policy", async (ArchonAI.Core.Models.Admin.PolicyConfiguration policy, IAdminService adminService, CancellationToken ct) =>
{
    await adminService.UpdatePolicyConfigAsync(policy, ct);
    return Results.Ok(new { updated = true });
});

admin.MapGet("/monitoring", async (IAdminService adminService, CancellationToken ct) =>
{
    var snapshot = await adminService.GetSystemSnapshotAsync(ct);
    return Results.Ok(snapshot);
});

// RBAC endpoints
var rbac = admin.MapGroup("/rbac");

rbac.MapGet("/status", (IRbacService rbacService) => Results.Ok(rbacService.GetStatus()));

rbac.MapGet("/roles", async (IRbacService rbacService, CancellationToken ct) =>
{
    var roles = await rbacService.GetRolesAsync(ct);
    return Results.Ok(roles);
});

rbac.MapGet("/roles/{roleId:guid}", async (Guid roleId, IRbacService rbacService, CancellationToken ct) =>
{
    var role = await rbacService.GetRoleAsync(roleId, ct);
    return role is null ? Results.NotFound() : Results.Ok(role);
});

rbac.MapPost("/roles", async (CreateRoleRequest request, IRbacService rbacService, CancellationToken ct) =>
{
    var role = await rbacService.CreateRoleAsync(request.Name, request.Description, request.Permissions, ct);
    return Results.Created($"/api/v1/admin/rbac/roles/{role.Id}", role);
});

rbac.MapPut("/roles/{roleId:guid}", async (Guid roleId, UpdateRoleRequest request, IRbacService rbacService, CancellationToken ct) =>
{
    await rbacService.UpdateRoleAsync(roleId, request.Description, request.Permissions, ct);
    return Results.Ok(new { roleId, updated = true });
});

rbac.MapDelete("/roles/{roleId:guid}", async (Guid roleId, IRbacService rbacService, CancellationToken ct) =>
{
    await rbacService.DeleteRoleAsync(roleId, ct);
    return Results.Ok(new { roleId, deleted = true });
});

rbac.MapGet("/assignments", async (string? subjectId, IRbacService rbacService, CancellationToken ct) =>
{
    var assignments = await rbacService.GetAssignmentsAsync(subjectId, ct);
    return Results.Ok(assignments);
});

rbac.MapPost("/assignments", async (AssignRoleRequest request, IRbacService rbacService, CancellationToken ct) =>
{
    var assignment = await rbacService.AssignRoleAsync(request.SubjectId, request.SubjectType, request.RoleId, request.AssignedBy, ct);
    return Results.Created($"/api/v1/admin/rbac/assignments/{assignment.Id}", assignment);
});

rbac.MapDelete("/assignments/{assignmentId:guid}", async (Guid assignmentId, IRbacService rbacService, CancellationToken ct) =>
{
    await rbacService.RevokeRoleAsync(assignmentId, ct);
    return Results.Ok(new { assignmentId, revoked = true });
});

rbac.MapGet("/policies", async (IRbacService rbacService, CancellationToken ct) =>
{
    var policies = await rbacService.GetPoliciesAsync(ct);
    return Results.Ok(policies);
});

rbac.MapPost("/policies", async (CreatePolicyRequest request, IRbacService rbacService, CancellationToken ct) =>
{
    var policy = await rbacService.CreatePolicyAsync(request.Name, request.Description, request.RequiredPermissions, request.Resource, request.Effect, request.Conditions.AsReadOnly(), ct);
    return Results.Created($"/api/v1/admin/rbac/policies/{policy.Id}", policy);
});

rbac.MapMethods("/policies/{policyId:guid}", ["PATCH"], async (Guid policyId, UpdatePolicyEnabledRequest request, IRbacService rbacService, CancellationToken ct) =>
{
    await rbacService.UpdatePolicyAsync(policyId, request.IsEnabled, ct);
    return Results.Ok(new { policyId, updated = true });
});

rbac.MapDelete("/policies/{policyId:guid}", async (Guid policyId, IRbacService rbacService, CancellationToken ct) =>
{
    await rbacService.DeletePolicyAsync(policyId, ct);
    return Results.Ok(new { policyId, deleted = true });
});

rbac.MapPost("/evaluate", async (EvaluateAccessRequest request, IRbacService rbacService, CancellationToken ct) =>
{
    var decision = await rbacService.EvaluateAccessAsync(request.SubjectId, request.Resource, request.Action, ct);
    return Results.Ok(decision);
});

rbac.MapGet("/permissions/{subjectId}", async (string subjectId, IRbacService rbacService, CancellationToken ct) =>
{
    var permissions = await rbacService.GetEffectivePermissionsAsync(subjectId, ct);
    return Results.Ok(permissions);
});

// Runtime health endpoints
var runtimeHealth = admin.MapGroup("/runtime/health");

runtimeHealth.MapGet("/", async (IRuntimeHealthManager healthManager, CancellationToken ct) =>
{
    var snapshot = await healthManager.GetHealthSnapshotAsync();
    return Results.Ok(snapshot);
});

runtimeHealth.MapGet("/policies", (IRuntimeHealthManager healthManager) =>
{
    var policies = healthManager.GetRecoveryPolicies();
    return Results.Ok(policies);
});

runtimeHealth.MapGet("/recoveries", async (int? limit, IRuntimeHealthManager healthManager, CancellationToken ct) =>
{
    var history = await healthManager.GetRecoveryHistoryAsync(limit ?? 50);
    return Results.Ok(history);
});

runtimeHealth.MapPost("/check", async (IRuntimeHealthManager healthManager, CancellationToken ct) =>
{
    await healthManager.RunHealthCheckAsync(ct);
    var snapshot = await healthManager.GetHealthSnapshotAsync();
    return Results.Ok(snapshot);
});

// Agent coordination endpoints
var coordination = v1.MapGroup("/coordination")
    .RequireAuthorization("OperatorOrAdmin");

coordination.MapGet("/status", (IAgentCoordinationService coordService) =>
    Results.Ok(coordService.GetStatus()));

coordination.MapPost("/support", async (RequestTaskSupportInput input, IAgentCoordinationService coordService, CancellationToken ct) =>
{
    var request = new ArchonAI.Core.Models.Coordination.TaskSupportRequest(
        Id: Guid.NewGuid(),
        RequestingAgentId: input.RequestingAgentId,
        RequestingAgentName: input.RequestingAgentName,
        TaskId: input.TaskId,
        RequiredCapability: input.RequiredCapability,
        Reason: input.Reason,
        Context: input.Context ?? new Dictionary<string, string>(),
        Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 60),
        RequestedAtUtc: DateTimeOffset.UtcNow);
    var response = await coordService.RequestTaskSupportAsync(request, ct);
    return Results.Ok(response);
});

coordination.MapPost("/knowledge", async (ShareKnowledgeInput input, IAgentCoordinationService coordService, CancellationToken ct) =>
{
    var payload = new ArchonAI.Core.Models.Coordination.KnowledgeSharePayload(
        Id: Guid.NewGuid(),
        SourceAgentId: input.SourceAgentId,
        SourceAgentName: input.SourceAgentName,
        TargetAgentId: input.TargetAgentId,
        Topic: input.Topic,
        Content: input.Content,
        Metadata: input.Metadata ?? new Dictionary<string, string>(),
        SharedAtUtc: DateTimeOffset.UtcNow);
    await coordService.ShareKnowledgeAsync(payload, ct);
    return Results.Ok(new { payloadId = payload.Id, shared = true });
});

coordination.MapPost("/delegate", async (DelegateTaskInput input, IAgentCoordinationService coordService, CancellationToken ct) =>
{
    var delegation = new ArchonAI.Core.Models.Coordination.TaskDelegation(
        Id: Guid.NewGuid(),
        DelegatingAgentId: input.DelegatingAgentId,
        DelegatingAgentName: input.DelegatingAgentName,
        TargetAgentId: input.TargetAgentId,
        TargetAgentName: input.TargetAgentName,
        OriginalTaskId: input.OriginalTaskId,
        RequiredCapability: input.RequiredCapability,
        TaskInputs: input.TaskInputs ?? new Dictionary<string, string>(),
        Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 60),
        DelegatedAtUtc: DateTimeOffset.UtcNow);
    var result = await coordService.DelegateTaskAsync(delegation, ct);
    return Results.Ok(result);
});

// ══════════════════════════════════════════════════════════════
//  Agent Collaboration
// ══════════════════════════════════════════════════════════════

var collaboration = v1.MapGroup("/collaboration")
    .RequireAuthorization("OperatorOrAdmin");

collaboration.MapGet("/status", (IAgentCollaborationManager collabManager) =>
    Results.Ok(collabManager.GetStatus()));

collaboration.MapPost("/sessions", async (CreateCollaborationSessionInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
{
    var session = await collabManager.CreateSessionAsync(
        input.InitiatorAgentId, input.InitiatorAgentName, input.Purpose, input.InitialContext, ct);
    return Results.Created($"/api/v1/collaboration/sessions/{session.SessionId}", session);
});

collaboration.MapGet("/sessions/{sessionId:guid}", async (Guid sessionId, IAgentCollaborationManager collabManager, CancellationToken ct) =>
{
    var session = await collabManager.GetSessionAsync(sessionId, ct);
    return session is null ? Results.NotFound() : Results.Ok(session);
});

collaboration.MapPost("/sessions/{sessionId:guid}/join", async (Guid sessionId, JoinCollaborationSessionInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
{
    var session = await collabManager.JoinSessionAsync(sessionId, input.AgentId, input.AgentName, input.Role, ct);
    return Results.Ok(session);
});

collaboration.MapPost("/sessions/{sessionId:guid}/context", async (Guid sessionId, ShareCollaborationContextInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
{
    await collabManager.ShareContextAsync(sessionId, input.AgentId, input.Context, ct);
    var session = await collabManager.GetSessionAsync(sessionId, ct);
    return Results.Ok(session);
});

collaboration.MapPost("/sessions/{sessionId:guid}/complete", async (Guid sessionId, IAgentCollaborationManager collabManager, CancellationToken ct) =>
{
    var session = await collabManager.CompleteSessionAsync(sessionId, ct);
    return Results.Ok(session);
});

collaboration.MapPost("/delegate-smart", async (SmartDelegationInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
{
    var request = new ArchonAI.Core.Models.Collaboration.SmartDelegationRequest(
        RequestId: Guid.NewGuid(),
        DelegatingAgentId: input.DelegatingAgentId,
        DelegatingAgentName: input.DelegatingAgentName,
        RequiredCapability: input.RequiredCapability,
        PreferredTaskType: input.PreferredTaskType,
        TaskInputs: input.TaskInputs ?? new Dictionary<string, string>(),
        FallbackAgentIds: input.FallbackAgentIds,
        Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 120),
        RequestedAtUtc: DateTimeOffset.UtcNow);
    var result = await collabManager.DelegateSmartAsync(request, ct);
    return Results.Ok(result);
});

collaboration.MapPost("/assist", async (AssistanceRequestInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
{
    var request = new ArchonAI.Core.Models.Collaboration.AssistanceRequest(
        RequestId: Guid.NewGuid(),
        RequestingAgentId: input.RequestingAgentId,
        RequestingAgentName: input.RequestingAgentName,
        Objective: input.Objective,
        RequiredCapabilities: input.RequiredCapabilities,
        Context: input.Context ?? new Dictionary<string, string>(),
        MaxResponders: input.MaxResponders ?? 5,
        Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 120),
        RequestedAtUtc: DateTimeOffset.UtcNow);
    var result = await collabManager.RequestAssistanceAsync(request, ct);
    return Results.Ok(result);
});

// Memory management endpoints
var memory = v1.MapGroup("/memory")
    .RequireAuthorization("OperatorOrAdmin");

memory.MapGet("/status", (IMemoryRetrievalOptimizer retriever) =>
    Results.Ok(retriever.GetStatus()));

memory.MapPost("/compress", async (CompressMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
{
    var result = await compressor.CompressAsync(request.Scope, ct);
    return Results.Ok(result);
});

memory.MapPost("/deduplicate", async (DeduplicateMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
{
    int removed = await compressor.DeduplicateAsync(request.Scope, ct);
    return Results.Ok(new { scope = request.Scope, duplicatesRemoved = removed });
});

memory.MapPost("/cluster", async (ClusterMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
{
    var clusters = await compressor.ClusterAsync(request.Scope, ct);
    return Results.Ok(clusters);
});

memory.MapPost("/summarize", async (SummarizeMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
{
    var summary = await compressor.SummarizeAsync(request.Scope, request.SourceRecordIds, ct);
    return Results.Ok(summary);
});

memory.MapPost("/search", async (SearchMemoryRequest request, IMemoryRetrievalOptimizer retriever, CancellationToken ct) =>
{
    var result = await retriever.SearchAsync(request.Scope, request.QueryEmbedding, request.TopK ?? 10, ct);
    return Results.Ok(result);
});

memory.MapPost("/index/rebuild", async (RebuildIndexRequest request, IMemoryRetrievalOptimizer retriever, CancellationToken ct) =>
{
    await retriever.RebuildIndexAsync(request.Scope, ct);
    return Results.Ok(new { scope = request.Scope, rebuilt = true });
});

// ══════════════════════════════════════════════════════════════
//  Continuous Improvement
// ══════════════════════════════════════════════════════════════

var continuousImprovement = v1.MapGroup("/continuous-improvement")
    .RequireAuthorization("OperatorOrAdmin");

continuousImprovement.MapPost("/cycle", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
{
    var report = await engine.RunCycleAsync(ct);
    return Results.Ok(report);
});

continuousImprovement.MapGet("/inefficiencies", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
{
    var inefficiencies = await engine.DetectInefficienciesAsync(ct);
    return Results.Ok(inefficiencies);
});

continuousImprovement.MapPost("/recommend", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
{
    var inefficiencies = await engine.DetectInefficienciesAsync(ct);
    var recommendations = await engine.RecommendImprovementsAsync(inefficiencies, ct);
    return Results.Ok(new { inefficiencies = inefficiencies.Count, recommendations });
});

continuousImprovement.MapGet("/trends", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
{
    var trends = await engine.GetTrendsAsync(ct);
    return Results.Ok(trends);
});

continuousImprovement.MapGet("/history", (IContinuousImprovementEngine engine) =>
{
    var history = engine.GetCycleHistory();
    return Results.Ok(history);
});

// ══════════════════════════════════════════════════════════════
//  Organizational Memory
// ══════════════════════════════════════════════════════════════

var orgMemory = v1.MapGroup("/org-memory")
    .RequireAuthorization("OperatorOrAdmin");

orgMemory.MapPost("/store", async (OrganizationalMemoryEntry entry, IOrganizationalMemoryStore store, CancellationToken ct) =>
{
    await store.StoreAsync(entry, ct);
    return Results.Ok(new { stored = true, entryId = entry.EntryId });
});

orgMemory.MapPost("/search", async (OrgMemorySearchRequest request, IOrganizationalMemoryStore store, CancellationToken ct) =>
{
    OrganizationalMemoryType? filterType = null;
    if (!string.IsNullOrWhiteSpace(request.FilterType)
        && Enum.TryParse<OrganizationalMemoryType>(request.FilterType, ignoreCase: true, out var parsed))
    {
        filterType = parsed;
    }
    var result = await store.SearchAsync(request.QueryText, filterType, request.FilterCategory, request.TopK ?? 10, ct);
    return Results.Ok(result);
});

orgMemory.MapGet("/timeline/{entryType}", async (string entryType, string? category, int? limit, IOrganizationalMemoryStore store, CancellationToken ct) =>
{
    if (!Enum.TryParse<OrganizationalMemoryType>(entryType, ignoreCase: true, out var parsed))
        return Results.BadRequest(new { error = $"Invalid entry type: {entryType}" });

    var timeline = await store.GetTimelineAsync(parsed, category, limit ?? 50, ct);
    return Results.Ok(timeline);
});

orgMemory.MapPost("/analyze", async (IOrganizationalMemoryStore store, CancellationToken ct) =>
{
    var report = await store.AnalyzeAsync(ct);
    return Results.Ok(report);
});

orgMemory.MapGet("/related/{knowledgeNodeId}", async (string knowledgeNodeId, IOrganizationalMemoryStore store, CancellationToken ct) =>
{
    var entries = await store.GetRelatedEntriesAsync(knowledgeNodeId, ct);
    return Results.Ok(entries);
});

// Observability endpoints
var observability = v1.MapGroup("/observability")
    .RequireAuthorization("OperatorOrAdmin");

observability.MapGet("/status", (IObservabilityService obsService) =>
{
    var status = obsService.GetStatus();
    return Results.Ok(status);
});

observability.MapGet("/traces", async (Guid? agentId, int? limit, IObservabilityService obsService, CancellationToken ct) =>
{
    var traces = await obsService.GetAgentTracesAsync(agentId, limit ?? 100, ct);
    return Results.Ok(traces);
});

observability.MapGet("/workflows/{workflowId:guid}/performance", async (Guid workflowId, IObservabilityService obsService, CancellationToken ct) =>
{
    var snapshot = await obsService.GetWorkflowPerformanceAsync(workflowId, ct);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
});

observability.MapGet("/system", async (IObservabilityService obsService, CancellationToken ct) =>
{
    var metrics = await obsService.GetSystemMetricsAsync(ct);
    return Results.Ok(metrics);
});

observability.MapGet("/connectors/health", async (IObservabilityService obsService, CancellationToken ct) =>
{
    var health = await obsService.GetConnectorHealthAsync(ct);
    return Results.Ok(health);
});

observability.MapGet("/agents/metrics", async (IObservabilityService obsService, CancellationToken ct) =>
{
    var metrics = await obsService.GetAgentMetricsSummariesAsync(ct);
    return Results.Ok(metrics);
});

// Workflow design endpoints
var workflows = v1.MapGroup("/workflows")
    .RequireAuthorization("OperatorOrAdmin");

workflows.MapGet("/status", (IWorkflowDesignService wfService) =>
    Results.Ok(wfService.GetStatus()));

workflows.MapGet("", async (WorkflowDesignStatus? status, int? offset, int? limit,
    IWorkflowDesignService wfService, CancellationToken ct) =>
{
    var list = await wfService.ListWorkflowsAsync(status, offset ?? 0, limit ?? 50, ct);
    return Results.Ok(list);
});

workflows.MapGet("/{workflowId:guid}", async (Guid workflowId, IWorkflowDesignService wfService, CancellationToken ct) =>
{
    var workflow = await wfService.GetWorkflowAsync(workflowId, ct);
    return workflow is null ? Results.NotFound() : Results.Ok(workflow);
});

workflows.MapPost("", async (CreateWorkflowRequest request, IWorkflowDesignService wfService, CancellationToken ct) =>
{
    var steps = request.Steps.Select(s => new WorkflowStepDefinition(
        s.Order, s.Name, s.Description, s.AgentType, s.Inputs)).ToList();

    var workflow = await wfService.CreateWorkflowAsync(
        request.Name, request.Description, request.Strategy, steps, request.Metadata, ct);

    return Results.Created($"/api/v1/workflows/{workflow.Id}", workflow);
});

workflows.MapPost("/{workflowId:guid}/validate", async (Guid workflowId, IWorkflowDesignService wfService, CancellationToken ct) =>
{
    var result = await wfService.ValidateWorkflowAsync(workflowId, ct);
    return Results.Ok(result);
});

workflows.MapPost("/{workflowId:guid}/execute", async (Guid workflowId, ExecuteWorkflowRequest request,
    IWorkflowDesignService wfService, CancellationToken ct) =>
{
    try
    {
        var summary = await wfService.ExecuteWorkflowAsync(workflowId, request.TenantId, request.Metadata, ct);
        return Results.Ok(summary);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

workflows.MapDelete("/{workflowId:guid}", async (Guid workflowId, IWorkflowDesignService wfService, CancellationToken ct) =>
{
    var deleted = await wfService.DeleteWorkflowAsync(workflowId, ct);
    return deleted ? Results.Ok(new { workflowId, deleted = true }) : Results.NotFound();
});

// Workflow designer endpoints
var designer = v1.MapGroup("/designer/workflows")
    .RequireAuthorization("OperatorOrAdmin");

designer.MapGet("", async (string? nameFilter, int? offset, int? limit,
    IWorkflowDesignerService designerService, CancellationToken ct) =>
{
    var list = await designerService.ListWorkflowGraphsAsync(nameFilter, offset ?? 0, limit ?? 50, ct);
    return Results.Ok(list);
});

designer.MapGet("/{graphId:guid}", async (Guid graphId,
    IWorkflowDesignerService designerService, CancellationToken ct) =>
{
    var graph = await designerService.GetWorkflowGraphAsync(graphId, ct);
    return graph is null ? Results.NotFound() : Results.Ok(graph);
});

designer.MapPost("", async (CreateWorkflowGraphRequest request,
    IWorkflowDesignerService designerService, CancellationToken ct) =>
{
    var nodes = request.Nodes.Select(n => new WorkflowNode(
        Guid.NewGuid(), n.Name, n.Description, n.NodeType, n.Configuration)).ToList();

    var nodeIdMap = nodes.Select((n, i) => (i, n.Id)).ToDictionary(x => x.i, x => x.Id);

    var edges = request.Edges.Select(e => new WorkflowEdge(
        Guid.NewGuid(),
        nodeIdMap.GetValueOrDefault(e.SourceNodeIndex),
        nodeIdMap.GetValueOrDefault(e.TargetNodeIndex),
        e.Label)).ToList();

    var graph = await designerService.CreateWorkflowGraphAsync(
        request.Name, request.Description, nodes, edges, request.Metadata, ct);

    return Results.Created($"/api/v1/designer/workflows/{graph.Id}", graph);
});

designer.MapPost("/{graphId:guid}/validate", async (Guid graphId,
    IWorkflowDesignerService designerService, CancellationToken ct) =>
{
    var result = await designerService.ValidateWorkflowGraphAsync(graphId, ct);
    return Results.Ok(result);
});

designer.MapPost("/{graphId:guid}/simulate", async (Guid graphId,
    IWorkflowDesignerService designerService, CancellationToken ct) =>
{
    try
    {
        var result = await designerService.SimulateWorkflowGraphAsync(graphId, ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

designer.MapPost("/{graphId:guid}/export", async (Guid graphId, string? version,
    IWorkflowDesignerService designerService, CancellationToken ct) =>
{
    try
    {
        var export = await designerService.ExportWorkflowGraphAsync(graphId, version ?? "1.0", ct);
        return Results.Ok(export);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

designer.MapDelete("/{graphId:guid}", async (Guid graphId,
    IWorkflowDesignerService designerService, CancellationToken ct) =>
{
    var deleted = await designerService.DeleteWorkflowGraphAsync(graphId, ct);
    return deleted ? Results.Ok(new { graphId, deleted = true }) : Results.NotFound();
});

// ── Durable workflow execution ──────────────────────────────
var executions = v1.MapGroup("/executions")
    .RequireAuthorization("OperatorOrAdmin");

executions.MapGet("", async (
    string? tenantId,
    WorkflowExecutionStatus? status,
    int? limit,
    IWorkflowExecutionStore store,
    CancellationToken ct) =>
{
    var list = await store.ListAsync(tenantId, status, limit ?? 50, ct);
    return Results.Ok(list);
});

executions.MapGet("/{workflowId:guid}", async (
    Guid workflowId,
    IWorkflowExecutionStore store,
    CancellationToken ct) =>
{
    var record = await store.GetAsync(workflowId, ct);
    return record is null ? Results.NotFound() : Results.Ok(record);
});

executions.MapPost("/{workflowId:guid}/cancel", async (
    Guid workflowId,
    DurableWorkflowExecutionEngine engine,
    CancellationToken ct) =>
{
    try
    {
        var record = await engine.CancelWorkflowAsync(workflowId, "Cancelled via API", ct);
        return Results.Ok(record);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});

executions.MapPost("/{workflowId:guid}/retry", async (
    Guid workflowId,
    DurableWorkflowExecutionEngine engine,
    CancellationToken ct) =>
{
    try
    {
        var record = await engine.RetryWorkflowAsync(workflowId, (_, _) =>
            global::System.Threading.Tasks.Task.FromResult(new ExecutionResult(
                Guid.Empty, false, "No executor configured for API retry",
                new Dictionary<string, string>(), Array.Empty<string>(),
                new[] { "Retry via API requires executor binding" },
                DateTimeOffset.UtcNow)), ct);
        return Results.Ok(record);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

executions.MapGet("/resumable", async (
    IWorkflowExecutionStore store,
    CancellationToken ct) =>
{
    var resumable = await store.GetResumableAsync(ct);
    return Results.Ok(resumable);
});

// Control plane endpoints
var controlPlane = v1.MapGroup("/control-plane")
    .RequireAuthorization("AdminOnly");

controlPlane.MapGet("/status", (IControlPlaneService cpService) =>
    Results.Ok(cpService.GetStatus()));

controlPlane.MapGet("/dashboard", async (IControlPlaneService cpService, CancellationToken ct) =>
{
    var dashboard = await cpService.GetDashboardAsync(ct);
    return Results.Ok(dashboard);
});

// ── Tenant lifecycle ──────────────────────────────────────────

var tenants = controlPlane.MapGroup("/tenants");

tenants.MapGet("", async (TenantStatus? status, int? offset, int? limit,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var list = await cpService.ListTenantsAsync(status, offset ?? 0, limit ?? 50, ct);
    return Results.Ok(list);
});

tenants.MapGet("/{tenantId:guid}", async (Guid tenantId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var tenant = await cpService.GetTenantAsync(tenantId, ct);
    return tenant is null ? Results.NotFound() : Results.Ok(tenant);
});

tenants.MapPost("", async (ProvisionTenantRequest request,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var tenant = await cpService.ProvisionTenantAsync(
            request.Name, request.DisplayName, request.Tier, request.Metadata, ct);
        return Results.Created($"/api/v1/control-plane/tenants/{tenant.Id}", tenant);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 409);
    }
});

tenants.MapPost("/{tenantId:guid}/activate", async (Guid tenantId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var tenant = await cpService.ActivateTenantAsync(tenantId, ct);
        return Results.Ok(tenant);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

tenants.MapPost("/{tenantId:guid}/suspend", async (Guid tenantId, SuspendTenantRequest request,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var tenant = await cpService.SuspendTenantAsync(tenantId, request.Reason, ct);
        return Results.Ok(tenant);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

tenants.MapDelete("/{tenantId:guid}", async (Guid tenantId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        await cpService.DeprovisionTenantAsync(tenantId, ct);
        return Results.Ok(new { tenantId, deprovisioned = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

// ── Workflow lifecycle ────────────────────────────────────────

var cpWorkflows = controlPlane.MapGroup("/workflows");

cpWorkflows.MapGet("", async (string? tenantId, ManagedWorkflowStatus? status,
    int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
{
    var list = await cpService.ListManagedWorkflowsAsync(tenantId, status, offset ?? 0, limit ?? 50, ct);
    return Results.Ok(list);
});

cpWorkflows.MapGet("/{workflowId:guid}", async (Guid workflowId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var workflow = await cpService.GetManagedWorkflowAsync(workflowId, ct);
    return workflow is null ? Results.NotFound() : Results.Ok(workflow);
});

cpWorkflows.MapPost("", async (RegisterWorkflowRequest request,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var workflow = await cpService.RegisterWorkflowAsync(
            request.TenantId, request.Name, request.Description,
            request.Strategy, request.StepCount, request.Metadata, ct);
        return Results.Created($"/api/v1/control-plane/workflows/{workflow.Id}", workflow);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

cpWorkflows.MapPatch("/{workflowId:guid}/status", async (Guid workflowId,
    UpdateManagedStatusRequest request, IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var workflow = await cpService.UpdateWorkflowStatusAsync(workflowId, request.Status, ct);
        return Results.Ok(workflow);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

// ── Agent lifecycle ───────────────────────────────────────────

var cpAgents = controlPlane.MapGroup("/agents");

cpAgents.MapGet("", async (string? tenantId, ManagedAgentStatus? status,
    int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
{
    var list = await cpService.ListManagedAgentsAsync(tenantId, status, offset ?? 0, limit ?? 50, ct);
    return Results.Ok(list);
});

cpAgents.MapGet("/{agentId:guid}", async (Guid agentId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var agent = await cpService.GetManagedAgentAsync(agentId, ct);
    return agent is null ? Results.NotFound() : Results.Ok(agent);
});

cpAgents.MapPost("", async (RegisterManagedAgentRequest request,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var agent = await cpService.RegisterAgentAsync(
            request.TenantId, request.Name, request.Version,
            request.Capabilities, request.Configuration, ct);
        return Results.Created($"/api/v1/control-plane/agents/{agent.Id}", agent);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

cpAgents.MapPatch("/{agentId:guid}/status", async (Guid agentId,
    UpdateManagedAgentStatusRequest request, IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var agent = await cpService.UpdateAgentStatusAsync(agentId, request.Status, ct);
        return Results.Ok(agent);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

cpAgents.MapDelete("/{agentId:guid}", async (Guid agentId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        await cpService.DeregisterAgentAsync(agentId, ct);
        return Results.Ok(new { agentId, deregistered = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

// ── Policy management ─────────────────────────────────────────

var cpPolicies = controlPlane.MapGroup("/policies");

cpPolicies.MapGet("", async (string? tenantId, PlatformPolicyType? policyType, bool? isEnabled,
    int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
{
    var list = await cpService.ListPoliciesAsync(tenantId, policyType, isEnabled,
        offset ?? 0, limit ?? 50, ct);
    return Results.Ok(list);
});

cpPolicies.MapGet("/{policyId:guid}", async (Guid policyId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var policy = await cpService.GetPolicyAsync(policyId, ct);
    return policy is null ? Results.NotFound() : Results.Ok(policy);
});

cpPolicies.MapPost("", async (CreatePlatformPolicyRequest request,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var policy = await cpService.CreatePolicyAsync(
        request.TenantId, request.Name, request.Description, request.PolicyType,
        request.TargetResource, request.Rules, request.Priority, ct);
    return Results.Created($"/api/v1/control-plane/policies/{policy.Id}", policy);
});

cpPolicies.MapPut("/{policyId:guid}", async (Guid policyId,
    UpdatePlatformPolicyRequest request, IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        var policy = await cpService.UpdatePolicyAsync(
            policyId, request.IsEnabled, request.Rules, request.Priority, ct);
        return Results.Ok(policy);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

cpPolicies.MapDelete("/{policyId:guid}", async (Guid policyId,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        await cpService.DeletePolicyAsync(policyId, ct);
        return Results.Ok(new { policyId, deleted = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

// ── Configuration management ──────────────────────────────────

var cpConfig = controlPlane.MapGroup("/config");

cpConfig.MapGet("", async (string? tenantId, string? scope,
    int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
{
    var list = await cpService.ListConfigurationsAsync(tenantId, scope,
        offset ?? 0, limit ?? 100, ct);
    return Results.Ok(list);
});

cpConfig.MapGet("/{tenantId}/{scope}/{key}", async (string tenantId, string scope, string key,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var config = await cpService.GetConfigurationAsync(tenantId, scope, key, ct);
    return config is null ? Results.NotFound() : Results.Ok(config);
});

cpConfig.MapPut("", async (SetConfigurationRequest request,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    var config = await cpService.SetConfigurationAsync(
        request.TenantId, request.Scope, request.Key, request.Value,
        request.Description, request.IsSecret, ct);
    return Results.Ok(config);
});

cpConfig.MapDelete("/{tenantId}/{scope}/{key}", async (string tenantId, string scope, string key,
    IControlPlaneService cpService, CancellationToken ct) =>
{
    try
    {
        await cpService.DeleteConfigurationAsync(tenantId, scope, key, ct);
        return Results.Ok(new { tenantId, scope, key, deleted = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

// ControlPlane observability dashboard endpoints
var cpDashboard = v1.MapGroup("/control-plane/observability")
    .RequireAuthorization("OperatorOrAdmin");

cpDashboard.MapGet("/unified", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    var dashboard = await cpObs.GetUnifiedDashboardAsync(ct);
    return Results.Ok(dashboard);
});

cpDashboard.MapGet("/agent-activity", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    var dashboard = await cpObs.GetAgentActivityAsync(ct);
    return Results.Ok(dashboard);
});

cpDashboard.MapGet("/system-health", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    var dashboard = await cpObs.GetSystemHealthAsync(ct);
    return Results.Ok(dashboard);
});

cpDashboard.MapGet("/model-usage", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    var dashboard = await cpObs.GetModelUsageAsync(ct);
    return Results.Ok(dashboard);
});

cpDashboard.MapGet("/task-performance", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    var dashboard = await cpObs.GetTaskPerformanceAsync(ct);
    return Results.Ok(dashboard);
});

// System control endpoints
var sysControl = v1.MapGroup("/control-plane/system")
    .RequireAuthorization("AdminOnly");

sysControl.MapGet("/paused", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    bool paused = await cpObs.IsSystemPausedAsync(ct);
    return Results.Ok(new { isPaused = paused });
});

sysControl.MapPost("/pause", async (SystemPauseRequest request, IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    await cpObs.PauseSystemAsync(request.Reason, ct);
    return Results.Ok(new { paused = true, reason = request.Reason, atUtc = DateTimeOffset.UtcNow });
});

sysControl.MapPost("/resume", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    await cpObs.ResumeSystemAsync(ct);
    return Results.Ok(new { resumed = true, atUtc = DateTimeOffset.UtcNow });
});

// Alert management endpoints
var alerts = v1.MapGroup("/control-plane/alerts")
    .RequireAuthorization("OperatorOrAdmin");

alerts.MapGet("/", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    var activeAlerts = await cpObs.GetActiveAlertsAsync(ct);
    return Results.Ok(activeAlerts);
});

alerts.MapPost("/{alertId:guid}/acknowledge", async (Guid alertId, IControlPlaneObservability cpObs, CancellationToken ct) =>
{
    await cpObs.AcknowledgeAlertAsync(alertId, ct);
    return Results.Ok(new { alertId, acknowledged = true });
});

alerts.MapPost("/raise", (RaiseAlertRequest request, IControlPlaneObservability cpObs) =>
{
    cpObs.RaiseAlert(request.Severity, request.Component, request.Message);
    return Results.Ok(new { raised = true, atUtc = DateTimeOffset.UtcNow });
}).RequireAuthorization("AdminOnly");

// Monitoring dashboard endpoints
var monitoring = v1.MapGroup("/monitoring")
    .RequireAuthorization("OperatorOrAdmin");

monitoring.MapGet("/status", (IMonitoringDashboardService monService) =>
    Results.Ok(monService.GetStatus()));

monitoring.MapGet("/dashboard", async (IMonitoringDashboardService monService, CancellationToken ct) =>
{
    var dashboard = await monService.GetFullDashboardAsync(ct);
    return Results.Ok(dashboard);
});

monitoring.MapGet("/workflows", async (IMonitoringDashboardService monService, CancellationToken ct) =>
{
    var metrics = await monService.GetWorkflowMetricsAsync(ct);
    return Results.Ok(metrics);
});

monitoring.MapGet("/agents", async (IMonitoringDashboardService monService, CancellationToken ct) =>
{
    var health = await monService.GetAgentHealthAsync(ct);
    return Results.Ok(health);
});

monitoring.MapGet("/system", async (IMonitoringDashboardService monService, CancellationToken ct) =>
{
    var performance = await monService.GetSystemPerformanceAsync(ct);
    return Results.Ok(performance);
});

// System insight endpoints
var insights = v1.MapGroup("/insights")
    .RequireAuthorization("OperatorOrAdmin");

insights.MapGet("/dashboard", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
{
    var dashboard = await insightEngine.GetDashboardAsync(ct);
    return Results.Ok(dashboard);
});

insights.MapGet("/health", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
{
    var health = await insightEngine.GetHealthSummaryAsync(ct);
    return Results.Ok(health);
});

insights.MapGet("/bottlenecks", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
{
    var bottlenecks = await insightEngine.DetectBottlenecksAsync(ct);
    return Results.Ok(bottlenecks);
});

insights.MapGet("/anomalies", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
{
    var anomalies = await insightEngine.DetectAnomaliesAsync(ct);
    return Results.Ok(anomalies);
});

insights.MapGet("/agents/load", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
{
    var load = await insightEngine.GetAgentLoadAsync(ct);
    return Results.Ok(load);
});

insights.MapGet("/models/latency", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
{
    var latency = await insightEngine.GetModelLatencyAsync(ct);
    return Results.Ok(latency);
});

insights.MapGet("/trends", async (string? component, int? limit, ISystemInsightEngine insightEngine, CancellationToken ct) =>
{
    var trends = await insightEngine.GetTrendsAsync(component, limit ?? 60, ct);
    return Results.Ok(trends);
});

insights.MapPost("/record/agent-load", (RecordAgentLoadRequest request, ISystemInsightEngine insightEngine) =>
{
    insightEngine.RecordAgentLoad(request.AgentId, request.AgentName, request.ActiveTasks,
        request.QueuedTasks, request.ExecutionTimeMs, request.CpuPercent, request.MemoryPercent);
    return Results.Ok(new { recorded = true });
});

insights.MapPost("/record/model-latency", (RecordModelLatencyRequest request, ISystemInsightEngine insightEngine) =>
{
    insightEngine.RecordModelLatency(request.Provider, request.Model, request.LatencyMs, request.Success);
    return Results.Ok(new { recorded = true });
});

// Security metrics endpoints
var security = admin.MapGroup("/security");

security.MapGet("/metrics", (ISecurityPolicyEngine securityEngine) =>
    Results.Ok(securityEngine.GetMetrics()));

security.MapGet("/policies", async (string? category, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
{
    var policies = await securityEngine.GetPoliciesAsync(category, ct);
    return Results.Ok(policies);
});

security.MapPost("/policies", async (AddSecurityPolicyRequest request, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new ArchonAI.Core.Models.Governance.SecurityPolicy(
        Id: Guid.NewGuid(),
        Name: request.Name,
        Category: request.Category,
        Rule: new ArchonAI.Core.Models.Governance.SecurityPolicyRule(
            request.RuleType,
            request.AllowedValues ?? Array.Empty<string>(),
            request.DeniedValues ?? Array.Empty<string>(),
            request.Limits ?? new Dictionary<string, string>()),
        IsEnabled: true,
        CreatedAtUtc: now,
        UpdatedAtUtc: now);
    await securityEngine.AddPolicyAsync(policy, ct);
    return Results.Created($"/api/v1/admin/security/policies/{policy.Id}", policy);
});

security.MapDelete("/policies/{policyId:guid}", async (Guid policyId, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
{
    await securityEngine.RemovePolicyAsync(policyId, ct);
    return Results.Ok(new { policyId, removed = true });
});

security.MapPost("/evaluate/data-access", async (EvaluateDataAccessRequest request, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
{
    var result = await securityEngine.EvaluateDataAccessAsync(request.SubjectId, request.ResourceType, request.Action, ct);
    return Results.Ok(result);
});

security.MapPost("/evaluate/workflow-limits", async (EvaluateWorkflowLimitsRequest request, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
{
    var result = await securityEngine.EvaluateWorkflowLimitsAsync(request.WorkflowId, request.StepCount, request.ConcurrentAgents, ct);
    return Results.Ok(result);
});

// Audit log endpoints
var audit = v1.MapGroup("/audit")
    .RequireAuthorization("OperatorOrAdmin");

audit.MapGet("/status", (IAuditLogService auditService) =>
{
    var status = auditService.GetStatus();
    return Results.Ok(status);
});

audit.MapGet("/entries", async (string? category, string? subjectId, string? resourceType,
    DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? offset, int? limit,
    IAuditLogService auditService, CancellationToken ct) =>
{
    var result = await auditService.QueryAsync(category, subjectId, resourceType, fromUtc, toUtc, offset ?? 0, limit ?? 100, ct);
    return Results.Ok(result);
});

audit.MapGet("/entries/{entryId:guid}", async (Guid entryId, IAuditLogService auditService, CancellationToken ct) =>
{
    var entry = await auditService.GetEntryAsync(entryId, ct);
    return entry is null ? Results.NotFound() : Results.Ok(entry);
});

audit.MapPost("/record", async (AuditRecordRequest request, IAuditLogService auditService, CancellationToken ct) =>
{
    var entry = await auditService.RecordAsync(
        request.EventType, request.Category, request.Source,
        request.SubjectId, request.SubjectType, request.Action,
        request.ResourceType, request.ResourceId, request.Description,
        request.Metadata, ct);
    return Results.Created($"/api/v1/audit/entries/{entry.Id}", entry);
}).RequireAuthorization("AdminOnly");

audit.MapPost("/verify", async (AuditVerifyRequest? request, IAuditLogService auditService, CancellationToken ct) =>
{
    var isValid = await auditService.VerifyIntegrityAsync(request?.FromEntryId, ct);
    return Results.Ok(new { integrityValid = isValid, verifiedAtUtc = DateTimeOffset.UtcNow });
});

// ── Agent Registry ──────────────────────────────────────────────────

var agentRegistry = v1.MapGroup("/agent-registry")
    .RequireAuthorization("OperatorOrAdmin");

agentRegistry.MapGet("/agents", async (
    RegisteredAgentStatus? status, string? capability, int? offset, int? limit,
    IAgentRegistryService arService, CancellationToken ct) =>
{
    var agents = await arService.ListAgentsAsync(status, capability, offset ?? 0, limit ?? 50, ct);
    return Results.Ok(agents);
});

agentRegistry.MapGet("/agents/{agentId:guid}", async (
    Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
{
    var agent = await arService.GetAgentAsync(agentId, ct);
    return agent is null ? Results.NotFound() : Results.Ok(agent);
});

agentRegistry.MapPost("/agents", async (
    RegisterAgentRequest request, IAgentRegistryService arService, CancellationToken ct) =>
{
    var capabilities = request.Capabilities.Select(c => new AgentCapabilityRecord(
        Id: Guid.Empty, AgentId: Guid.Empty,
        Name: c.Name, Description: c.Description,
        Category: c.Category, Version: c.Version,
        AddedAtUtc: default)).ToList();

    var agent = await arService.RegisterAgentAsync(
        request.Name, request.Description, request.Version,
        capabilities, request.Configuration, ct);
    return Results.Created($"/api/v1/agent-registry/agents/{agent.Id}", agent);
});

agentRegistry.MapPut("/agents/{agentId:guid}/capabilities", async (
    Guid agentId, UpdateAgentCapabilitiesRequest request,
    IAgentRegistryService arService, CancellationToken ct) =>
{
    try
    {
        var capabilities = request.Capabilities.Select(c => new AgentCapabilityRecord(
            Id: Guid.Empty, AgentId: agentId,
            Name: c.Name, Description: c.Description,
            Category: c.Category, Version: c.Version,
            AddedAtUtc: default)).ToList();

        var agent = await arService.UpdateCapabilitiesAsync(agentId, capabilities, ct);
        return Results.Ok(agent);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

agentRegistry.MapPost("/agents/{agentId:guid}/enable", async (
    Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
{
    try
    {
        var agent = await arService.EnableAgentAsync(agentId, ct);
        return Results.Ok(agent);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

agentRegistry.MapPost("/agents/{agentId:guid}/disable", async (
    Guid agentId, DisableAgentRequest request,
    IAgentRegistryService arService, CancellationToken ct) =>
{
    try
    {
        var agent = await arService.DisableAgentAsync(agentId, request.Reason, ct);
        return Results.Ok(agent);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

agentRegistry.MapPost("/agents/{agentId:guid}/heartbeat", async (
    Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
{
    try
    {
        await arService.RecordHeartbeatAsync(agentId, ct);
        return Results.Ok(new { agentId, heartbeatAtUtc = DateTimeOffset.UtcNow });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

agentRegistry.MapPost("/agents/{agentId:guid}/metrics", async (
    Guid agentId, RecordAgentMetricsRequest request,
    IAgentRegistryService arService, CancellationToken ct) =>
{
    try
    {
        var metric = await arService.RecordMetricsAsync(
            agentId, request.TotalExecutions, request.SuccessfulExecutions,
            request.FailedExecutions, request.AverageLatencyMs,
            request.P95LatencyMs, request.UptimePercent, ct);
        return Results.Created($"/api/v1/agent-registry/agents/{agentId}/metrics", metric);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

agentRegistry.MapGet("/agents/{agentId:guid}/metrics", async (
    Guid agentId, int? limit,
    IAgentRegistryService arService, CancellationToken ct) =>
{
    var metrics = await arService.GetMetricsAsync(agentId, limit ?? 20, ct);
    return Results.Ok(metrics);
});

agentRegistry.MapGet("/dashboard", async (
    IAgentRegistryService arService, CancellationToken ct) =>
{
    var dashboard = await arService.GetDashboardAsync(ct);
    return Results.Ok(dashboard);
});

agentRegistry.MapDelete("/agents/{agentId:guid}", async (
    Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
{
    try
    {
        await arService.DeregisterAgentAsync(agentId, ct);
        return Results.Ok(new { agentId, deregistered = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

// ── Workflow Simulation ─────────────────────────────────────────────

var workflowSim = v1.MapGroup("/workflow-simulation")
    .RequireAuthorization("OperatorOrAdmin");

workflowSim.MapPost("/simulate", async (
    SimulateWorkflowRequest request,
    IWorkflowSimulationService wsService, CancellationToken ct) =>
{
    try
    {
        var result = await wsService.SimulateAsync(
            request.WorkflowGraphId, request.StrategyId,
            request.HistoricalOverrides, ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

workflowSim.MapGet("/predict/{workflowGraphId:guid}", async (
    Guid workflowGraphId,
    IWorkflowSimulationService wsService, CancellationToken ct) =>
{
    try
    {
        var prediction = await wsService.PredictOutcomesAsync(workflowGraphId, ct);
        return Results.Ok(prediction);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

workflowSim.MapGet("/resources/{workflowGraphId:guid}", async (
    Guid workflowGraphId, Guid? strategyId,
    IWorkflowSimulationService wsService, CancellationToken ct) =>
{
    try
    {
        var estimate = await wsService.EstimateResourcesAsync(workflowGraphId, strategyId, ct);
        return Results.Ok(estimate);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

workflowSim.MapGet("/latency/{workflowGraphId:guid}", async (
    Guid workflowGraphId,
    IWorkflowSimulationService wsService, CancellationToken ct) =>
{
    try
    {
        var estimate = await wsService.EstimateLatencyAsync(workflowGraphId, ct);
        return Results.Ok(estimate);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

workflowSim.MapPost("/history", async (
    RecordHistoricalExecutionRequest request,
    IWorkflowSimulationService wsService, CancellationToken ct) =>
{
    await wsService.RecordHistoricalExecutionAsync(
        request.WorkflowGraphId, request.IsSuccess,
        request.LatencyMs, request.Cost, ct);
    return Results.Ok(new { request.WorkflowGraphId, recorded = true });
});

workflowSim.MapGet("/history/{workflowGraphId:guid}", async (
    Guid workflowGraphId,
    IWorkflowSimulationService wsService, CancellationToken ct) =>
{
    var data = await wsService.GetHistoricalDataAsync(workflowGraphId, ct);
    return data is null ? Results.NotFound() : Results.Ok(data);
});

// ══════════════════════════════════════════════════════════════
//  Strategy Simulation (TaskGraph-based)
// ══════════════════════════════════════════════════════════════

var strategySim = v1.MapGroup("/strategy-simulation")
    .RequireAuthorization("OperatorOrAdmin");

strategySim.MapPost("/simulate", async (SimulateStrategyRequest req, IStrategySimulator simulator, ITaskGraphBuilder graphBuilder, CancellationToken ct) =>
{
    var graph = await graphBuilder.GetGraphAsync(req.GraphId, ct);
    if (graph is null)
        return Results.NotFound(new { error = $"Task graph '{req.GraphId}' not found." });

    var result = await simulator.SimulateAsync(graph, req.Strategy, ct);
    return Results.Ok(result);
});

strategySim.MapPost("/compare", async (CompareTaskGraphStrategiesRequest req, IStrategySimulator simulator, ITaskGraphBuilder graphBuilder, CancellationToken ct) =>
{
    var graph = await graphBuilder.GetGraphAsync(req.GraphId, ct);
    if (graph is null)
        return Results.NotFound(new { error = $"Task graph '{req.GraphId}' not found." });

    var result = await simulator.CompareStrategiesAsync(graph, req.Strategies, ct);
    return Results.Ok(result);
});

strategySim.MapPost("/simulate-goal", async (SimulateGoalStrategiesRequest req, IStrategySimulator simulator, IGoalGenerator goalGenerator, CancellationToken ct) =>
{
    var goal = await goalGenerator.GetGoalAsync(req.GoalId, ct);
    if (goal is null)
        return Results.NotFound(new { error = $"Goal '{req.GoalId}' not found." });

    var result = await simulator.SimulateGoalStrategiesAsync(goal, req.Strategies, ct);
    return Results.Ok(result);
});

strategySim.MapPost("/guided-plan", async (SimulationGuidedPlanRequest req, IStrategicPlanner planner, IGoalGenerator goalGenerator, CancellationToken ct) =>
{
    var goal = await goalGenerator.GetGoalAsync(req.GoalId, ct);
    if (goal is null)
        return Results.NotFound(new { error = $"Goal '{req.GoalId}' not found." });

    var plan = await planner.BuildSimulationGuidedPlanAsync(goal, req.Strategies, ct);
    return Results.Ok(plan);
});

// ── Strategy Library ────────────────────────────────────────────────

var strategyLib = v1.MapGroup("/strategy-library")
    .RequireAuthorization("OperatorOrAdmin");

strategyLib.MapGet("/strategies", async (
    string? objectiveType, string? tag, int? offset, int? limit,
    IStrategyLibraryService slService, CancellationToken ct) =>
{
    var strategies = await slService.ListStrategiesAsync(objectiveType, tag, offset ?? 0, limit ?? 50, ct);
    return Results.Ok(strategies);
});

strategyLib.MapGet("/strategies/{strategyId:guid}", async (
    Guid strategyId, IStrategyLibraryService slService, CancellationToken ct) =>
{
    var strategy = await slService.GetStrategyAsync(strategyId, ct);
    return strategy is null ? Results.NotFound() : Results.Ok(strategy);
});

strategyLib.MapPost("/strategies", async (
    CreateStrategyTemplateRequest request,
    IStrategyLibraryService slService, CancellationToken ct) =>
{
    var resourceUsage = new StrategyResourceUsage(
        request.ResourceUsage.EstimatedCpuSeconds,
        request.ResourceUsage.EstimatedMemoryMb,
        request.ResourceUsage.EstimatedAgentCount,
        request.ResourceUsage.EstimatedCostPerExecution,
        request.ResourceUsage.CostCurrency);

    var strategy = await slService.CreateStrategyAsync(
        request.Name, request.Description, request.ObjectiveType,
        request.WorkflowTemplate, request.SuccessMetrics,
        resourceUsage, request.Tags, request.Metadata, ct);
    return Results.Created($"/api/v1/strategy-library/strategies/{strategy.Id}", strategy);
});

strategyLib.MapPut("/strategies/{strategyId:guid}", async (
    Guid strategyId, UpdateStrategyTemplateRequest request,
    IStrategyLibraryService slService, CancellationToken ct) =>
{
    try
    {
        StrategyResourceUsage? resourceUsage = request.ResourceUsage is not null
            ? new StrategyResourceUsage(
                request.ResourceUsage.EstimatedCpuSeconds,
                request.ResourceUsage.EstimatedMemoryMb,
                request.ResourceUsage.EstimatedAgentCount,
                request.ResourceUsage.EstimatedCostPerExecution,
                request.ResourceUsage.CostCurrency)
            : null;

        var strategy = await slService.UpdateStrategyAsync(
            strategyId, request.Description, request.WorkflowTemplate,
            request.SuccessMetrics, resourceUsage, request.Tags, ct);
        return Results.Ok(strategy);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

strategyLib.MapDelete("/strategies/{strategyId:guid}", async (
    Guid strategyId, IStrategyLibraryService slService, CancellationToken ct) =>
{
    try
    {
        await slService.DeleteStrategyAsync(strategyId, ct);
        return Results.Ok(new { strategyId, deleted = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

strategyLib.MapPost("/strategies/{strategyId:guid}/executions", async (
    Guid strategyId, RecordStrategyExecutionRequest request,
    IStrategyLibraryService slService, CancellationToken ct) =>
{
    try
    {
        var record = await slService.RecordExecutionAsync(
            strategyId, request.IsSuccess, request.LatencyMs,
            request.Cost, request.Outcomes, ct);
        return Results.Created(
            $"/api/v1/strategy-library/strategies/{strategyId}/executions", record);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

strategyLib.MapGet("/ranked", async (
    string objectiveType, int? limit,
    IStrategyLibraryService slService, CancellationToken ct) =>
{
    var ranked = await slService.GetRankedStrategiesAsync(objectiveType, limit ?? 10, ct);
    return Results.Ok(ranked);
});

strategyLib.MapPost("/compare", async (
    CompareStrategiesRequest request,
    IStrategyLibraryService slService, CancellationToken ct) =>
{
    try
    {
        var result = await slService.CompareStrategiesAsync(request.StrategyIds, ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

// ── Cluster Management ──────────────────────────────────────────────

var cluster = v1.MapGroup("/cluster")
    .RequireAuthorization("OperatorOrAdmin");

cluster.MapGet("/status", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    var status = await clusterCoord.GetClusterStatusAsync(ct);
    return Results.Ok(status);
});

cluster.MapGet("/dashboard", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    var dashboard = await clusterCoord.GetDashboardAsync(ct);
    return Results.Ok(dashboard);
});

cluster.MapGet("/nodes", async (ClusterNodeStatus? status, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    var nodes = await clusterCoord.ListNodesAsync(status, ct);
    return Results.Ok(nodes);
});

cluster.MapGet("/nodes/{nodeId:guid}", async (Guid nodeId, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    var node = await clusterCoord.GetNodeAsync(nodeId, ct);
    return node is null ? Results.NotFound() : Results.Ok(node);
});

cluster.MapPost("/nodes", async (RegisterClusterNodeRequest request, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    try
    {
        var capacity = new NodeCapacity(
            request.MaxConcurrentTasks, request.MaxAgents,
            request.CpuCores, request.MemoryBytes, request.GpuSlots);

        var node = await clusterCoord.RegisterNodeAsync(
            request.HostName, request.Role, capacity,
            request.Capabilities, request.Labels, ct);
        return Results.Created($"/api/v1/cluster/nodes/{node.NodeId}", node);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

cluster.MapPost("/nodes/{nodeId:guid}/heartbeat", async (
    Guid nodeId, NodeHeartbeatRequest request, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    try
    {
        var load = new NodeLoad(
            request.ActiveTasks, request.QueuedTasks, request.ActiveAgents,
            request.CpuUtilizationPercent, request.MemoryUtilizationPercent,
            request.GpuSlotsUsed, DateTimeOffset.UtcNow);

        var node = await clusterCoord.HeartbeatAsync(nodeId, load, ct);
        return Results.Ok(node);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

cluster.MapPost("/nodes/{nodeId:guid}/drain", async (
    Guid nodeId, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    try
    {
        await clusterCoord.DrainNodeAsync(nodeId, ct);
        return Results.Ok(new { nodeId, status = "draining" });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

cluster.MapDelete("/nodes/{nodeId:guid}", async (
    Guid nodeId, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    try
    {
        await clusterCoord.RemoveNodeAsync(nodeId, ct);
        return Results.Ok(new { nodeId, removed = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

cluster.MapPost("/schedule", async (
    ClusterScheduleRequest request, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    var result = await clusterCoord.ScheduleOnClusterAsync(request, ct);
    return Results.Ok(result);
});

cluster.MapPost("/schedule/batch", async (
    IReadOnlyList<ClusterScheduleRequest> requests, IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    var results = await clusterCoord.ScheduleBatchAsync(requests, ct);
    return Results.Ok(results);
});

cluster.MapGet("/distribution-plan", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    var plan = await clusterCoord.GenerateDistributionPlanAsync(ct);
    return Results.Ok(plan);
});

cluster.MapPost("/rebalance", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
{
    await clusterCoord.RebalanceAsync(ct);
    return Results.Ok(new { rebalanced = true, atUtc = DateTimeOffset.UtcNow });
}).RequireAuthorization("AdminOnly");

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

// ══════════════════════════════════════════════════════════════
//  Business Perception Engine
// ══════════════════════════════════════════════════════════════

var perception = v1.MapGroup("/perception")
    .RequireAuthorization("OperatorOrAdmin");

perception.MapPost("/signals", async (IngestSignalRequest req, ISignalIngestionService ingestion, CancellationToken ct) =>
{
    var signal = new BusinessSignal(
        SignalId: Guid.NewGuid(),
        SignalType: Enum.Parse<SignalType>(req.SignalType, true),
        SourceSystem: Enum.Parse<SourceSystem>(req.SourceSystem, true),
        EntityId: req.EntityId,
        Timestamp: req.Timestamp ?? DateTimeOffset.UtcNow,
        Payload: req.Payload);

    var result = await ingestion.IngestApiSignalAsync(signal, ct);
    return result.Accepted ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

perception.MapPost("/signals/batch", async (IngestSignalBatchRequest req, ISignalIngestionService ingestion, CancellationToken ct) =>
{
    var signals = req.Signals.Select(s => new BusinessSignal(
        SignalId: Guid.NewGuid(),
        SignalType: Enum.Parse<SignalType>(s.SignalType, true),
        SourceSystem: Enum.Parse<SourceSystem>(s.SourceSystem, true),
        EntityId: s.EntityId,
        Timestamp: s.Timestamp ?? DateTimeOffset.UtcNow,
        Payload: s.Payload)).ToList();

    var source = Enum.Parse<SourceSystem>(req.Signals[0].SourceSystem, true);
    var results = await ingestion.IngestScheduledPollSignalsAsync(source, signals, ct);
    return Results.Ok(results);
});

perception.MapGet("/dashboard", async (IBusinessPerceptionEngine engine, CancellationToken ct) =>
{
    var dashboard = await engine.GetDashboardAsync(ct);
    return Results.Ok(dashboard);
});

perception.MapPost("/polling/start", async (ISignalIngestionService ingestion, CancellationToken ct) =>
{
    await ingestion.StartPollingAsync(ct);
    return Results.Ok(new { polling = "started" });
}).RequireAuthorization("AdminOnly");

perception.MapPost("/polling/stop", async (ISignalIngestionService ingestion, CancellationToken ct) =>
{
    await ingestion.StopPollingAsync(ct);
    return Results.Ok(new { polling = "stopped" });
}).RequireAuthorization("AdminOnly");

// ══════════════════════════════════════════════════════════════
//  Organization State
// ══════════════════════════════════════════════════════════════

var orgState = v1.MapGroup("/organization-state")
    .RequireAuthorization("OperatorOrAdmin");

orgState.MapGet("", async (IOrganizationStateEngine engine, CancellationToken ct) =>
{
    var state = await engine.GetCurrentStateAsync(ct);
    return Results.Ok(state);
});

orgState.MapGet("/dashboard", async (IOrganizationStateEngine engine, CancellationToken ct) =>
{
    var dashboard = await engine.GetDashboardAsync(ct);
    return Results.Ok(dashboard);
});

orgState.MapGet("/departments/{departmentId}", async (string departmentId, IOrganizationStateEngine engine, CancellationToken ct) =>
{
    var dept = await engine.GetDepartmentStateAsync(departmentId, ct);
    return dept is null ? Results.NotFound() : Results.Ok(dept);
});

orgState.MapGet("/departments/{departmentId}/resources", async (string departmentId, IOrganizationStateEngine engine, CancellationToken ct) =>
{
    var resources = await engine.GetResourcesByDepartmentAsync(departmentId, ct);
    return Results.Ok(resources);
});

orgState.MapGet("/customers/{customerId}", async (string customerId, IOrganizationStateEngine engine, CancellationToken ct) =>
{
    var customer = await engine.GetCustomerStateAsync(customerId, ct);
    return customer is null ? Results.NotFound() : Results.Ok(customer);
});

orgState.MapGet("/customers/by-health/{status}", async (string status, IOrganizationStateEngine engine, CancellationToken ct) =>
{
    var healthStatus = Enum.Parse<CustomerHealthStatus>(status, true);
    var customers = await engine.GetCustomersByHealthAsync(healthStatus, ct);
    return Results.Ok(customers);
});

orgState.MapPost("/snapshots", async (IOrganizationStateEngine engine, CancellationToken ct) =>
{
    var snapshot = await engine.TakeSnapshotAsync(ct);
    return Results.Ok(new { snapshotId = snapshot.SnapshotId, version = snapshot.Version });
}).RequireAuthorization("AdminOnly");

// ══════════════════════════════════════════════════════════════
//  Goal Generator
// ══════════════════════════════════════════════════════════════

var goals = v1.MapGroup("/goals")
    .RequireAuthorization("OperatorOrAdmin");

goals.MapPost("/generate", async (IGoalGenerator generator, CancellationToken ct) =>
{
    var result = await generator.GenerateGoalsAsync(ct);
    return Results.Ok(result);
}).RequireAuthorization("AdminOnly");

goals.MapGet("/dashboard", async (IGoalGenerator generator, CancellationToken ct) =>
{
    var dashboard = await generator.GetDashboardAsync(ct);
    return Results.Ok(dashboard);
});

goals.MapGet("/{goalId:guid}", async (Guid goalId, IGoalGenerator generator, CancellationToken ct) =>
{
    var goal = await generator.GetGoalAsync(goalId, ct);
    return goal is null ? Results.NotFound() : Results.Ok(goal);
});

goals.MapGet("/by-status/{status}", async (string status, IGoalGenerator generator, CancellationToken ct) =>
{
    var goalStatus = Enum.Parse<GoalStatus>(status, true);
    var result = await generator.GetGoalsByStatusAsync(goalStatus, ct);
    return Results.Ok(result);
});

goals.MapPost("/{goalId:guid}/approve", async (Guid goalId, IGoalGenerator generator, CancellationToken ct) =>
{
    await generator.ApproveGoalAsync(goalId, ct);
    return Results.Ok(new { goalId, status = "approved" });
}).RequireAuthorization("AdminOnly");

goals.MapPost("/{goalId:guid}/cancel", async (Guid goalId, CancelGoalRequest req, IGoalGenerator generator, CancellationToken ct) =>
{
    await generator.CancelGoalAsync(goalId, req.Reason, ct);
    return Results.Ok(new { goalId, status = "cancelled" });
}).RequireAuthorization("AdminOnly");

// ══════════════════════════════════════════════════════════════
//  Economic Evaluator
// ══════════════════════════════════════════════════════════════

var economics = v1.MapGroup("/economics")
    .RequireAuthorization("OperatorOrAdmin");

economics.MapPost("/evaluate", async (EconomicEvaluationRequest req, IEconomicEvaluator evaluator, IStrategicPlanner planner, CancellationToken ct) =>
{
    var objective = new Objective(
        Id: Guid.NewGuid(),
        Title: req.ObjectiveTitle,
        Description: req.ObjectiveDescription,
        Constraints: req.Constraints ?? new Dictionary<string, string>(),
        CreatedAtUtc: DateTimeOffset.UtcNow,
        DueAtUtc: req.Deadline);

    var workflow = await planner.BuildWorkflowAsync(objective, ct);

    var strategies = req.CandidateStrategies is { Count: > 0 }
        ? req.CandidateStrategies
        : (IReadOnlyList<string>)new[] { "balanced", "safe-mode", "throughput-optimized", "cost-optimized" };

    var weights = req.Weights is not null
        ? new EconomicWeights(req.Weights.CostWeight, req.Weights.ImpactWeight, req.Weights.SuccessProbabilityWeight, req.Weights.ExecutionTimeWeight)
        : null;

    var result = await evaluator.EvaluateStrategiesAsync(objective, workflow, strategies, weights, ct);
    return Results.Ok(result);
});

economics.MapPost("/evaluate-single", async (SingleStrategyEvaluationRequest req, IEconomicEvaluator evaluator, IStrategicPlanner planner, CancellationToken ct) =>
{
    var objective = new Objective(
        Id: Guid.NewGuid(),
        Title: req.ObjectiveTitle,
        Description: req.ObjectiveDescription,
        Constraints: req.Constraints ?? new Dictionary<string, string>(),
        CreatedAtUtc: DateTimeOffset.UtcNow,
        DueAtUtc: req.Deadline);

    var workflow = await planner.BuildWorkflowAsync(objective, ct);
    var result = await evaluator.EvaluateSingleStrategyAsync(objective, workflow, req.Strategy, ct);
    return Results.Ok(result);
});

// ══════════════════════════════════════════════════════════════
//  Outcome Evaluation
// ══════════════════════════════════════════════════════════════

var outcomeEval = v1.MapGroup("/outcome-evaluation")
    .RequireAuthorization("OperatorOrAdmin");

outcomeEval.MapPost("/evaluate", async (EvaluateOutcomeRequest req, IOutcomeEvaluator evaluator, IStrategySimulator simulator, ITaskGraphBuilder graphBuilder, CancellationToken ct) =>
{
    var graph = await graphBuilder.GetGraphAsync(req.SimulationResult.GraphId, ct);
    if (graph is null)
        return Results.NotFound(new { error = $"Task graph '{req.SimulationResult.GraphId}' not found." });

    var result = await evaluator.EvaluateAsync(req.SimulationResult, req.ActualResult, ct);
    return Results.Ok(result);
});

outcomeEval.MapGet("/goal/{goalId:guid}", async (Guid goalId, IOutcomeEvaluator evaluator, CancellationToken ct) =>
{
    var evaluations = await evaluator.GetEvaluationsForGoalAsync(goalId, ct);
    return Results.Ok(evaluations);
});

outcomeEval.MapGet("/strategy/{strategy}", async (string strategy, IOutcomeEvaluator evaluator, CancellationToken ct) =>
{
    var evaluations = await evaluator.GetEvaluationsForStrategyAsync(strategy, ct);
    return Results.Ok(evaluations);
});

// ══════════════════════════════════════════════════════════════
//  Model Routing
// ══════════════════════════════════════════════════════════════

var modelRouting = v1.MapGroup("/model-routing")
    .RequireAuthorization("OperatorOrAdmin");

modelRouting.MapPost("/adjust-weights", async (AdaptiveRoutingWeightEngine engine, CancellationToken ct) =>
{
    var report = await engine.AdjustWeightsAsync(ct);
    return Results.Ok(report);
});

modelRouting.MapGet("/weights", (IModelPerformanceTracker tracker) =>
{
    var weights = tracker.GetRoutingWeights();
    return Results.Ok(weights);
});

modelRouting.MapGet("/weights/{taskType}", (string taskType, IModelPerformanceTracker tracker) =>
{
    var weights = tracker.GetTaskTypeWeights(taskType);
    return Results.Ok(weights);
});

modelRouting.MapGet("/scores", (IModelPerformanceTracker tracker) =>
{
    var scores = tracker.GetAllScores();
    return Results.Ok(scores);
});

modelRouting.MapGet("/scores/{provider}/{model}", (string provider, string model, IModelPerformanceTracker tracker) =>
{
    var score = tracker.GetScore(provider, model);
    return score is not null ? Results.Ok(score) : Results.NotFound();
});

modelRouting.MapPost("/select", (
    string strategy,
    string? taskType,
    IModelPerformanceTracker tracker) =>
{
    var selected = tracker.SelectByWeight(strategy, taskType);
    if (selected is null)
        return Results.NotFound(new { message = "No eligible models found" });

    var weight = tracker.GetRoutingWeight(selected.Provider, selected.Model);
    return Results.Ok(new
    {
        selected.Provider,
        selected.Model,
        selected.CompositeScore,
        selected.SuccessRate,
        selected.AverageLatencyMs,
        selected.AverageCostPerRequest,
        RoutingWeight = weight?.Weight
    });
});

modelRouting.MapGet("/capabilities", () =>
{
    return Results.Ok(ArchonAI.Core.Models.Models.ModelCapabilityRegistry.All);
});

modelRouting.MapGet("/providers/status", (IModelProvider provider) =>
{
    // Return provider type info — actual health requires a live call
    return Results.Ok(new
    {
        providerType = provider.ProviderName,
        availableModels = ArchonAI.Core.Models.Models.ModelCapabilityRegistry.All.Keys
    });
});

// ══════════════════════════════════════════════════════════════
//  Onboarding
// ══════════════════════════════════════════════════════════════

var onboarding = v1.MapGroup("/onboarding")
    .RequireAuthorization("OperatorOrAdmin");

onboarding.MapPost("/deploy", async (
    ArchonAI.Core.Models.Onboarding.OnboardingDeployRequest req,
    IOnboardingService onboardingSvc,
    CancellationToken ct) =>
{
    var result = await onboardingSvc.DeployAsync(req, ct);
    return Results.Ok(result);
});

onboarding.MapPost("/deploy-template", async (
    ArchonAI.Core.Models.Onboarding.OnboardingTemplateDeployRequest req,
    IOnboardingService onboardingSvc,
    CancellationToken ct) =>
{
    var result = await onboardingSvc.DeployTemplateAsync(req, ct);
    return Results.Ok(result);
});

// ══════════════════════════════════════════════════════════════
//  Integration Marketplace
// ══════════════════════════════════════════════════════════════

var integrations = v1.MapGroup("/integrations")
    .RequireAuthorization("OperatorOrAdmin");

integrations.MapGet("/", (IEnumerable<IConnector> connectors) =>
{
    var list = connectors.Select(c => new
    {
        id = c.SystemName.ToLowerInvariant().Replace(' ', '-'),
        name = c.SystemName,
        status = "registered"
    });
    return Results.Ok(list);
});

integrations.MapPost("/{connectorId}/connect", async (
    string connectorId,
    IEnumerable<IConnector> connectors,
    CancellationToken ct) =>
{
    // Authenticate the matching connector
    var connector = connectors.FirstOrDefault(c =>
        c.SystemName.Equals(connectorId, StringComparison.OrdinalIgnoreCase) ||
        c.SystemName.ToLowerInvariant().Replace(' ', '-') == connectorId);

    if (connector is null)
        return Results.NotFound(new { error = $"Connector '{connectorId}' not found." });

    // Attempt to authenticate based on connector type
    if (connector is ISalesforceConnector sf)
    {
        var result = await sf.AuthenticateAsync(ct);
        return result.IsAuthenticated ? Results.Ok(new { status = "connected" }) : Results.Problem(result.Error ?? "Authentication failed");
    }
    if (connector is IHubSpotConnector hs)
    {
        var result = await hs.AuthenticateAsync(ct);
        return result.IsAuthenticated ? Results.Ok(new { status = "connected" }) : Results.Problem(result.Error ?? "Authentication failed");
    }
    if (connector is ISlackConnector sl)
    {
        var result = await sl.AuthenticateAsync(ct);
        return result.IsAuthenticated ? Results.Ok(new { status = "connected" }) : Results.Problem(result.Error ?? "Authentication failed");
    }
    if (connector is IQuickBooksConnector qb)
    {
        var result = await qb.AuthenticateAsync(ct);
        return result.IsAuthenticated ? Results.Ok(new { status = "connected" }) : Results.Problem(result.Error ?? "Authentication failed");
    }
    if (connector is IMicrosoft365Connector m365)
    {
        var result = await m365.AuthenticateAsync(ct);
        return result.IsAuthenticated ? Results.Ok(new { status = "connected" }) : Results.Problem(result.Error ?? "Authentication failed");
    }
    if (connector is IGoogleWorkspaceConnector gw)
    {
        var result = await gw.AuthenticateAsync(ct);
        return result.IsAuthenticated ? Results.Ok(new { status = "connected" }) : Results.Problem(result.Error ?? "Authentication failed");
    }

    return Results.Ok(new { status = "connected", note = "Basic connector — no authentication required." });
});

integrations.MapPost("/{connectorId}/disconnect", async (
    string connectorId,
    HttpContext ctx,
    IEnumerable<IConnector> connectors,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var connector = connectors.FirstOrDefault(c =>
        c.SystemName.Equals(connectorId, StringComparison.OrdinalIgnoreCase) ||
        c.SystemName.ToLowerInvariant().Replace(' ', '-') == connectorId);

    if (connector is null)
        return Results.NotFound(new { error = $"Connector '{connectorId}' not found." });

    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value
        ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (tenantId is null || userId is null) return Results.Unauthorized();

    if (await gov.RequiresApprovalAsync("connector.disconnect", ct))
    {
        var payload = JsonSerializer.Serialize(new { connectorId });
        var gate = await gov.RequestApprovalAsync(
            "connector.disconnect", connectorId, tenantId, userId,
            "Connector disconnect requested", payload, ct);
        return Results.Accepted($"/api/v1/governance/{gate.Id}", gate);
    }

    return Results.Ok(new { status = "disconnected" });
});

// ══════════════════════════════════════════════════════════════
//  Strategy Learning
// ══════════════════════════════════════════════════════════════

var strategyLearning = v1.MapGroup("/strategy-learning")
    .RequireAuthorization("OperatorOrAdmin");

strategyLearning.MapPost("/analyze", async (IStrategyLearningEngine learningEngine, CancellationToken ct) =>
{
    var report = await learningEngine.AnalyzeAndLearnAsync(ct);
    return Results.Ok(report);
});

strategyLearning.MapGet("/strategy-profiles", async (IStrategyLearningEngine learningEngine, CancellationToken ct) =>
{
    var profiles = await learningEngine.GetStrategyProfilesAsync(ct);
    return Results.Ok(profiles);
});

strategyLearning.MapGet("/agent-type-profiles", async (IStrategyLearningEngine learningEngine, CancellationToken ct) =>
{
    var profiles = await learningEngine.GetAgentTypeProfilesAsync(ct);
    return Results.Ok(profiles);
});

strategyLearning.MapGet("/recommend/{department}/{priority}", async (
    string department, string priority,
    IStrategyLearningEngine learningEngine, CancellationToken ct) =>
{
    var strategy = await learningEngine.RecommendStrategyAsync(department, priority, ct);
    return Results.Ok(new { department, priority, recommendedStrategy = strategy });
});

// ══════════════════════════════════════════════════════════════
//  Task Graphs
// ══════════════════════════════════════════════════════════════

var taskGraphs = v1.MapGroup("/task-graphs")
    .RequireAuthorization("OperatorOrAdmin");

taskGraphs.MapPost("/build", async (BuildTaskGraphRequest req, ITaskGraphBuilder builder, IGoalGenerator goalGenerator, CancellationToken ct) =>
{
    var goal = await goalGenerator.GetGoalAsync(req.GoalId, ct);
    if (goal is null)
        return Results.NotFound(new { error = $"Goal '{req.GoalId}' not found." });

    var graph = await builder.BuildGraphAsync(goal, req.Strategy ?? "balanced", ct);
    return Results.Ok(graph);
});

taskGraphs.MapPost("/{graphId:guid}/dispatch", async (Guid graphId, ITaskGraphBuilder builder, CancellationToken ct) =>
{
    var graph = await builder.GetGraphAsync(graphId, ct);
    if (graph is null)
        return Results.NotFound(new { error = $"Task graph '{graphId}' not found." });

    var result = await builder.DispatchGraphAsync(graph, ct);
    return Results.Ok(result);
}).RequireAuthorization("AdminOnly");

taskGraphs.MapGet("/{graphId:guid}", async (Guid graphId, ITaskGraphBuilder builder, CancellationToken ct) =>
{
    var graph = await builder.GetGraphAsync(graphId, ct);
    return graph is null ? Results.NotFound() : Results.Ok(graph);
});

taskGraphs.MapGet("/by-goal/{goalId:guid}", async (Guid goalId, ITaskGraphBuilder builder, CancellationToken ct) =>
{
    var graphs = await builder.GetGraphsByGoalAsync(goalId, ct);
    return Results.Ok(graphs);
});

taskGraphs.MapGet("/{graphId:guid}/layers", async (Guid graphId, ITaskGraphBuilder builder, CancellationToken ct) =>
{
    var graph = await builder.GetGraphAsync(graphId, ct);
    if (graph is null)
        return Results.NotFound();

    var layers = graph.GetExecutionLayers();
    return Results.Ok(new
    {
        graphId,
        totalLayers = layers.Count,
        totalNodes = graph.Nodes.Count,
        layers = layers.Select((layer, index) => new
        {
            layerIndex = index,
            parallelNodes = layer.Select(n => new { n.NodeId, n.Name, n.AgentType, n.ExpectedOutput })
        })
    });
});

// ── Explanation Engine ─────────────────────────────────────────────────
var explanations = v1.MapGroup("/explanations")
    .RequireAuthorization("OperatorOrAdmin");

explanations.MapPost("/strategy", async (
    ExplainStrategyRequest req,
    IExplanationEngine explanationEngine,
    CancellationToken ct) =>
{
    var result = await explanationEngine.ExplainStrategyChoiceAsync(
        req.GoalId, req.GoalTitle, req.CandidateStrategies, ct);
    return Results.Ok(result);
});

explanations.MapPost("/agent", async (
    ExplainAgentRequest req,
    IExplanationEngine explanationEngine,
    CancellationToken ct) =>
{
    var result = await explanationEngine.ExplainAgentSelectionAsync(
        req.RequiredCapability, req.TaskType, ct);
    return Results.Ok(result);
});

explanations.MapPost("/decision", async (
    ExplainDecisionRequest req,
    IExplanationEngine explanationEngine,
    CancellationToken ct) =>
{
    var result = await explanationEngine.ExplainDecisionAsync(
        req.GoalId, req.GoalTitle, req.CandidateStrategies,
        req.RequiredCapability, req.TaskType, ct);
    return Results.Ok(result);
});

// ── Human Override / Intervention ──────────────────────────────────────
var overrides = v1.MapGroup("/overrides")
    .RequireAuthorization("OperatorOrAdmin");

overrides.MapPost("/pause", async (
    PauseWorkflowRequest req,
    IHumanOverrideService overrideSvc,
    CancellationToken ct) =>
{
    var result = await overrideSvc.PauseWorkflowAsync(req, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

overrides.MapPost("/resume", async (
    ResumeWorkflowRequest req,
    IHumanOverrideService overrideSvc,
    CancellationToken ct) =>
{
    var result = await overrideSvc.ResumeWorkflowAsync(req, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

overrides.MapPost("/cancel", async (
    CancelActionRequest req,
    IHumanOverrideService overrideSvc,
    CancellationToken ct) =>
{
    var result = await overrideSvc.CancelActionAsync(req, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

overrides.MapPost("/modify-strategy", async (
    ModifyStrategyRequest req,
    HttpContext ctx,
    IHumanOverrideService overrideSvc,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value
        ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (tenantId is null || userId is null) return Results.Unauthorized();

    if (await gov.RequiresApprovalAsync("strategy.override", ct))
    {
        var payload = JsonSerializer.Serialize(req);
        var gate = await gov.RequestApprovalAsync(
            "strategy.override", req.WorkflowId.ToString(), tenantId, userId,
            "Strategy override requested", payload, ct);
        return Results.Accepted($"/api/v1/governance/{gate.Id}", gate);
    }

    var result = await overrideSvc.ModifyStrategyAsync(req, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

overrides.MapPost("/rollback", async (
    RollbackRequest req,
    IHumanOverrideService overrideSvc,
    CancellationToken ct) =>
{
    var result = await overrideSvc.RollbackAsync(req, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

overrides.MapGet("/log", async (
    Guid? workflowId,
    int? limit,
    IHumanOverrideService overrideSvc,
    CancellationToken ct) =>
{
    var log = await overrideSvc.GetOverrideLogAsync(workflowId, limit ?? 100, ct);
    return Results.Ok(log);
});

// ── Governance / Approval Gates ───────────────────────────
var governance = v1.MapGroup("/governance")
    .WithTags("governance");

governance.MapGet("/policies", async (
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var policies = await gov.ListApprovalPoliciesAsync(ct);
    return Results.Ok(policies);
}).RequireAuthorization("GovernanceRead");

governance.MapPost("/policies", async (
    CreateApprovalPolicyRequest req,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var policy = await gov.CreateApprovalPolicyAsync(
        req.ActionType, req.Description, req.RequiredApproverRole,
        req.RequireSeparationOfDuties, ct);
    return Results.Created($"/api/v1/governance/policies/{policy.Id}", policy);
}).RequireAuthorization("GovernanceWrite");

governance.MapPost("/request", async (
    RequestApprovalRequest req,
    HttpContext ctx,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value;
    if (tenantId is null || userId is null) return Results.Unauthorized();

    var gate = await gov.RequestApprovalAsync(
        req.ActionType, req.ResourceId, tenantId, userId, req.Justification, ct: ct);
    return Results.Ok(gate);
}).RequireAuthorization("OperatorOrAdmin");

governance.MapGet("/pending", async (
    HttpContext ctx,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantId is null) return Results.Unauthorized();

    var pending = await gov.ListPendingApprovalsAsync(tenantId, ct);
    return Results.Ok(pending);
}).RequireAuthorization("GovernanceRead");

governance.MapGet("/{gateId:guid}", async (
    Guid gateId,
    HttpContext ctx,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantId is null) return Results.Unauthorized();

    var gate = await gov.GetApprovalAsync(gateId, tenantId, ct);
    return gate is null ? Results.NotFound() : Results.Ok(gate);
}).RequireAuthorization("GovernanceRead");

governance.MapPost("/{gateId:guid}/review", async (
    Guid gateId,
    ReviewApprovalRequest req,
    HttpContext ctx,
    IGovernanceService gov,
    IGatedActionExecutor executor,
    CancellationToken ct) =>
{
    var reviewerId = ctx.User?.FindFirst("sub")?.Value
        ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    var reviewerRole = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
        ?? ctx.User?.FindFirst("role")?.Value;
    if (reviewerId is null || tenantId is null) return Results.Unauthorized();

    try
    {
        var gate = await gov.ReviewApprovalAsync(
            gateId, tenantId, reviewerId, reviewerRole ?? "Viewer", req.Approve, req.Notes, ct);

        // Execute the gated action on approval
        if (gate.Status == ApprovalStatus.Approved && gate.ActionPayload is not null)
        {
            try
            {
                var result = await executor.ExecuteAsync(gate, ct);
                gate = await gov.RecordExecutionResultAsync(
                    gate.Id,
                    result.Success ? GateExecutionStatus.Succeeded : GateExecutionStatus.Failed,
                    result.Error, ct);
            }
            catch (Exception ex)
            {
                gate = await gov.RecordExecutionResultAsync(
                    gate.Id, GateExecutionStatus.Failed, ex.Message, ct);
            }
        }

        return Results.Ok(gate);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 403);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
}).RequireAuthorization("GovernanceApprove");

governance.MapGet("/check/{actionType}", async (
    string actionType,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var required = await gov.RequiresApprovalAsync(actionType, ct);
    return Results.Ok(new { actionType, requiresApproval = required });
}).RequireAuthorization("GovernanceRead");

governance.MapGet("/history", async (
    string? actionType,
    int? limit,
    HttpContext ctx,
    IGovernanceService gov,
    CancellationToken ct) =>
{
    var callerTenant = ctx.User?.FindFirst("tenant_id")?.Value;
    if (callerTenant is null) return Results.Unauthorized();

    var history = await gov.GetApprovalHistoryAsync(callerTenant, actionType, limit ?? 50, ct);
    return Results.Ok(history);
}).RequireAuthorization("GovernanceRead");

// ── Trust-Tiered Autonomy ─────────────────────────────────
var trustTiers = v1.MapGroup("/trust-tiers")
    .WithTags("trust-tiers");

trustTiers.MapGet("/policies", async (
    HttpContext ctx,
    ITrustTierService trustSvc,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantId is null) return Results.Unauthorized();

    var policies = await trustSvc.ListPoliciesAsync(tenantId, ct);
    return Results.Ok(policies);
}).RequireAuthorization("GovernanceRead");

trustTiers.MapPost("/policies", async (
    SetTrustTierPolicyRequest req,
    HttpContext ctx,
    ITrustTierService trustSvc,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
    if (tenantId is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(req.ActionScope))
        return Results.BadRequest(new { error = "ActionScope is required." });

    if (!Enum.TryParse<ExecutionTrustTier>(req.MaxTier, true, out var maxTier))
        return Results.BadRequest(new { error = $"Invalid tier: {req.MaxTier}." });

    var now = DateTimeOffset.UtcNow;
    var policy = new TrustTierPolicy(
        Id: req.Id ?? Guid.NewGuid(),
        TenantId: tenantId,
        ActionScope: req.ActionScope,
        MaxTier: maxTier,
        ConfidenceThreshold: req.ConfidenceThreshold,
        ValueCeiling: req.ValueCeiling,
        RequireReversible: req.RequireReversible ?? false,
        Description: req.Description,
        IsEnabled: req.IsEnabled ?? true,
        CreatedBy: userId,
        CreatedAtUtc: now,
        UpdatedAtUtc: now);

    var created = await trustSvc.SetPolicyAsync(policy, ct);
    return Results.Created($"/api/v1/trust-tiers/policies/{created.Id}", created);
}).RequireAuthorization("GovernanceWrite");

trustTiers.MapDelete("/policies/{policyId:guid}", async (
    Guid policyId,
    HttpContext ctx,
    ITrustTierService trustSvc,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantId is null) return Results.Unauthorized();

    var deleted = await trustSvc.DeletePolicyAsync(policyId, tenantId, ct);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("GovernanceWrite");

trustTiers.MapPost("/evaluate", async (
    EvaluateTrustTierRequest req,
    HttpContext ctx,
    ITrustTierService trustSvc,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantId is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(req.ActionScope))
        return Results.BadRequest(new { error = "ActionScope is required." });

    if (!Enum.TryParse<ExecutionTrustTier>(req.RequestedTier, true, out var requestedTier))
        return Results.BadRequest(new { error = $"Invalid tier: {req.RequestedTier}." });

    var evaluation = await trustSvc.EvaluateAsync(
        tenantId, req.ActionScope, requestedTier,
        req.Confidence, req.Value, req.Reversible, ct);
    return Results.Ok(evaluation);
}).RequireAuthorization("GovernanceRead");

trustTiers.MapGet("/map", async (
    HttpContext ctx,
    ITrustTierService trustSvc,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantId is null) return Results.Unauthorized();

    var map = await trustSvc.GetTierMapAsync(tenantId, ct);
    return Results.Ok(map);
}).RequireAuthorization("GovernanceRead");

trustTiers.MapGet("/effective/{actionScope}", async (
    string actionScope,
    HttpContext ctx,
    ITrustTierService trustSvc,
    CancellationToken ct) =>
{
    var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantId is null) return Results.Unauthorized();

    var tier = await trustSvc.GetEffectiveTierAsync(tenantId, actionScope, ct);
    return Results.Ok(new { actionScope, effectiveTier = tier.ToString(), tierLevel = (int)tier });
}).RequireAuthorization("GovernanceRead");

// ── Permissions introspection ─────────────────────────────
v1.MapGet("/auth/permissions", async (
    HttpContext ctx,
    IRbacService rbac,
    CancellationToken ct) =>
{
    var userId = ctx.User?.FindFirst("sub")?.Value;
    if (userId is null) return Results.Unauthorized();

    var roleClaim = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
        ?? ctx.User?.FindFirst("role")?.Value;

    var storePerms = await rbac.GetEffectivePermissionsAsync(userId, ct);

    // Merge JWT role permissions
    var all = new HashSet<string>(storePerms);
    if (roleClaim is not null)
    {
        var rolePerms = roleClaim switch
        {
            "Admin" => new[] { "agents:read", "agents:write", "agents:execute", "workflows:read", "workflows:write", "workflows:execute", "connectors:read", "connectors:write", "connectors:execute", "admin:read", "admin:write", "policy:read", "policy:write", "monitoring:read", "rbac:read", "rbac:write", "governance:read", "governance:write", "governance:approve" },
            "Operator" => new[] { "agents:read", "agents:execute", "workflows:read", "workflows:execute", "connectors:read", "connectors:execute", "monitoring:read", "policy:read", "rbac:read", "governance:read" },
            "Viewer" => new[] { "agents:read", "workflows:read", "connectors:read", "monitoring:read", "policy:read", "rbac:read", "governance:read" },
            _ => Array.Empty<string>()
        };
        foreach (var p in rolePerms) all.Add(p);
    }

    return Results.Ok(new { userId, role = roleClaim, permissions = all.Order().ToList() });
}).RequireAuthorization();

// ── Decision Engine ───────────────────────────────────────────
var decisions = v1.MapGroup("/decisions")
    .RequireAuthorization("OperatorOrAdmin");

decisions.MapPost("/", async (
    CreateDecisionRequest req,
    IDecisionService decisionService,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Domain))
        return Results.BadRequest(new { error = "Title and domain are required." });

    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    var alternatives = req.Alternatives?.Select(a => new DecisionAlternative(
        Guid.NewGuid().ToString("N")[..8], a.Title, a.Rationale,
        a.Pros ?? Array.Empty<string>(), a.Cons ?? Array.Empty<string>(),
        a.EstimatedConfidence, a.EstimatedValue)).ToList()
        ?? new List<DecisionAlternative>();

    var decision = new DecisionRecord(
        Id: Guid.NewGuid(),
        TenantId: tenantId,
        Title: req.Title,
        Domain: req.Domain,
        Objective: req.Objective ?? string.Empty,
        Constraints: req.Constraints ?? Array.Empty<string>(),
        Assumptions: req.Assumptions ?? Array.Empty<string>(),
        Alternatives: alternatives,
        RecommendedOptionId: req.RecommendedOptionId ?? string.Empty,
        Confidence: Math.Clamp(req.Confidence ?? 0.0, 0.0, 1.0),
        Reversibility: Enum.TryParse<DecisionReversibility>(req.Reversibility, true, out var rev) ? rev : DecisionReversibility.PartiallyReversible,
        RiskLevel: Enum.TryParse<DecisionRiskLevel>(req.RiskLevel, true, out var risk) ? risk : DecisionRiskLevel.Medium,
        ExpectedValue: req.ExpectedValue,
        RequiresApproval: req.RequiresApproval ?? (risk >= DecisionRiskLevel.High),
        LinkedArtifacts: Array.Empty<DecisionLink>(),
        Status: DecisionStatus.Draft,
        CreatedBy: userId,
        CreatedAtUtc: DateTimeOffset.UtcNow,
        UpdatedAtUtc: DateTimeOffset.UtcNow);

    var created = await decisionService.CreateAsync(decision, ct);
    return Results.Created($"/api/v1/decisions/{created.Id}", created);
});

decisions.MapGet("/", async (
    IDecisionService decisionService,
    HttpContext httpContext,
    string? domain,
    string? status,
    int? limit,
    CancellationToken ct) =>
{
    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
    var parsedStatus = Enum.TryParse<DecisionStatus>(status, true, out var s) ? s : (DecisionStatus?)null;

    var results = await decisionService.ListAsync(tenantId, domain, parsedStatus, limit ?? 50, ct);
    return Results.Ok(results);
});

decisions.MapGet("/{decisionId:guid}", async (
    Guid decisionId,
    IDecisionService decisionService,
    CancellationToken ct) =>
{
    var decision = await decisionService.GetAsync(decisionId, ct);
    return decision is null ? Results.NotFound() : Results.Ok(decision);
});

decisions.MapPut("/{decisionId:guid}/status", async (
    Guid decisionId,
    UpdateDecisionStatusRequest req,
    IDecisionService decisionService,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Status))
        return Results.BadRequest(new { error = "Status is required." });

    if (!Enum.TryParse<DecisionStatus>(req.Status, true, out var newStatus))
        return Results.BadRequest(new { error = $"Invalid status: {req.Status}." });

    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
    var updated = await decisionService.UpdateStatusAsync(decisionId, newStatus, userId, req.Detail, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

decisions.MapPost("/{decisionId:guid}/links", async (
    Guid decisionId,
    CreateDecisionLinkRequest req,
    IDecisionService decisionService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ArtifactType) || string.IsNullOrWhiteSpace(req.ArtifactId))
        return Results.BadRequest(new { error = "ArtifactType and ArtifactId are required." });

    var link = new DecisionLink(req.ArtifactType, req.ArtifactId,
        req.Description ?? string.Empty, DateTimeOffset.UtcNow);

    var updated = await decisionService.LinkArtifactAsync(decisionId, link, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

decisions.MapGet("/{decisionId:guid}/history", async (
    Guid decisionId,
    IDecisionService decisionService,
    CancellationToken ct) =>
{
    var events = await decisionService.GetHistoryAsync(decisionId, ct);
    return Results.Ok(events);
});

// ── Financial Consequence Engine ──────────────────────────────
decisions.MapPost("/{decisionId:guid}/financial-consequence", async (
    Guid decisionId,
    AttachFinancialConsequenceRequest req,
    IDecisionService decisionService,
    IFinancialConsequenceService finService,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var decision = await decisionService.GetAsync(decisionId, ct);
    if (decision is null) return Results.NotFound();

    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    var consequence = new FinancialConsequence(
        Id: Guid.NewGuid(),
        DecisionId: decisionId,
        TenantId: decision.TenantId,
        ExpectedRevenueImpactLow: req.ExpectedRevenueImpactLow,
        ExpectedRevenueImpactHigh: req.ExpectedRevenueImpactHigh,
        ExpectedCostImpactLow: req.ExpectedCostImpactLow,
        ExpectedCostImpactHigh: req.ExpectedCostImpactHigh,
        ExpectedMarginImpact: req.ExpectedMarginImpact,
        ExpectedCashTimingImpact: req.ExpectedCashTimingImpact,
        LaborImpact: req.LaborImpact,
        DownsideRisk: req.DownsideRisk,
        UpsidePotential: req.UpsidePotential,
        ConfidenceAdjustment: req.ConfidenceAdjustment,
        RoiEstimateLow: req.RoiEstimateLow,
        RoiEstimateHigh: req.RoiEstimateHigh,
        BreakEvenEstimate: req.BreakEvenEstimate,
        Assumptions: req.Assumptions ?? Array.Empty<string>(),
        Notes: req.Notes,
        CreatedBy: userId,
        CreatedAtUtc: DateTimeOffset.UtcNow,
        UpdatedAtUtc: DateTimeOffset.UtcNow);

    var created = await finService.AttachAsync(consequence, ct);
    return Results.Created($"/api/v1/decisions/{decisionId}/financial-consequence", created);
});

decisions.MapGet("/{decisionId:guid}/financial-consequence", async (
    Guid decisionId,
    IFinancialConsequenceService finService,
    CancellationToken ct) =>
{
    var consequence = await finService.GetByDecisionAsync(decisionId, ct);
    return consequence is null ? Results.NotFound() : Results.Ok(consequence);
});

decisions.MapPut("/{decisionId:guid}/financial-consequence", async (
    Guid decisionId,
    AttachFinancialConsequenceRequest req,
    IFinancialConsequenceService finService,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var existing = await finService.GetByDecisionAsync(decisionId, ct);
    if (existing is null) return Results.NotFound();

    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    var updated = existing with
    {
        ExpectedRevenueImpactLow = req.ExpectedRevenueImpactLow,
        ExpectedRevenueImpactHigh = req.ExpectedRevenueImpactHigh,
        ExpectedCostImpactLow = req.ExpectedCostImpactLow,
        ExpectedCostImpactHigh = req.ExpectedCostImpactHigh,
        ExpectedMarginImpact = req.ExpectedMarginImpact,
        ExpectedCashTimingImpact = req.ExpectedCashTimingImpact,
        LaborImpact = req.LaborImpact,
        DownsideRisk = req.DownsideRisk,
        UpsidePotential = req.UpsidePotential,
        ConfidenceAdjustment = req.ConfidenceAdjustment,
        RoiEstimateLow = req.RoiEstimateLow,
        RoiEstimateHigh = req.RoiEstimateHigh,
        BreakEvenEstimate = req.BreakEvenEstimate,
        Assumptions = req.Assumptions ?? existing.Assumptions,
        Notes = req.Notes ?? existing.Notes,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    var result = await finService.UpdateAsync(decisionId, updated, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

// ── Outcome Learning ──────────────────────────────────────
var outcomes = v1.MapGroup("/outcomes")
    .WithTags("outcome-learning");

outcomes.MapPost("/expected", async (
    RecordExpectedOutcomeRequest req,
    HttpContext ctx,
    IOutcomeLearningService outcomeSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var record = await outcomeSvc.RecordExpectedOutcomeAsync(
        req.DecisionId, tenantId,
        req.ExpectedSummary, req.ExpectedValue,
        req.ConfidenceAtPrediction, req.ExpectedTimeframe,
        userId, ct);
    return Results.Created($"/api/v1/outcomes/{req.DecisionId}", record);
}).RequireAuthorization("OperatorOrAdmin");

outcomes.MapPost("/actual", async (
    RecordActualOutcomeRequest req,
    HttpContext ctx,
    IOutcomeLearningService outcomeSvc,
    CancellationToken ct) =>
{
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
    try
    {
        var record = await outcomeSvc.RecordActualOutcomeAsync(
            req.DecisionId, req.ActualSummary, req.ActualValue,
            req.RootCause, req.Notes, userId, ct);
        return Results.Ok(record);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "No expected outcome found for this decision." });
    }
}).RequireAuthorization("OperatorOrAdmin");

outcomes.MapGet("/{decisionId:guid}", async (
    Guid decisionId,
    IOutcomeLearningService outcomeSvc,
    CancellationToken ct) =>
{
    var record = await outcomeSvc.GetOutcomeAsync(decisionId, ct);
    return record is null ? Results.NotFound() : Results.Ok(record);
}).RequireAuthorization("GovernanceRead");

outcomes.MapGet("/", async (
    int? limit,
    HttpContext ctx,
    IOutcomeLearningService outcomeSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var records = await outcomeSvc.ListOutcomesAsync(tenantId, limit ?? 50, ct);
    return Results.Ok(records);
}).RequireAuthorization("GovernanceRead");

outcomes.MapGet("/calibration", async (
    string? domain,
    HttpContext ctx,
    IOutcomeLearningService outcomeSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await outcomeSvc.GetCalibrationSummaryAsync(tenantId, domain, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

// ── Proof Analytics ───────────────────────────────────────
var proof = v1.MapGroup("/proof-analytics")
    .WithTags("proof-analytics");

proof.MapPost("/events", async (
    RecordProofEventRequest req,
    HttpContext ctx,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    if (!Enum.TryParse<ProofEventType>(req.EventType, true, out var eventType))
        return Results.BadRequest(new { error = $"Invalid event type: {req.EventType}." });

    var proofEvent = new ProofEvent(
        Id: Guid.NewGuid(),
        TenantId: tenantId,
        DecisionId: req.DecisionId,
        WorkflowId: req.WorkflowId,
        EventType: eventType,
        Actor: userId,
        Detail: req.Detail,
        ExpectedValue: req.ExpectedValue,
        ActualValue: req.ActualValue,
        Variance: req.Variance,
        VariancePercent: req.VariancePercent,
        ActionType: req.ActionType,
        IsSuccess: req.IsSuccess,
        OverrideReason: req.OverrideReason,
        EconomicImpact: req.EconomicImpact,
        ImpactAttribution: req.ImpactAttribution,
        OccurredAtUtc: DateTimeOffset.UtcNow);

    var created = await proofSvc.RecordEventAsync(proofEvent, ct);
    return Results.Created($"/api/v1/proof-analytics/timeline/{req.DecisionId}", created);
}).RequireAuthorization("OperatorOrAdmin");

proof.MapGet("/timeline/{decisionId:guid}", async (
    Guid decisionId,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var timeline = await proofSvc.GetTimelineAsync(decisionId, ct);
    return timeline is null ? Results.NotFound() : Results.Ok(timeline);
}).RequireAuthorization("GovernanceRead");

proof.MapGet("/workflow/{workflowId:guid}/timelines", async (
    Guid workflowId,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var timelines = await proofSvc.GetWorkflowTimelinesAsync(workflowId, ct);
    return Results.Ok(timelines);
}).RequireAuthorization("GovernanceRead");

proof.MapGet("/predicted-vs-actual", async (
    string? domain, int? limit,
    HttpContext ctx,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await proofSvc.GetPredictedVsActualAsync(tenantId, domain, limit ?? 50, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

proof.MapGet("/approval-conversion", async (
    HttpContext ctx,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await proofSvc.GetApprovalConversionAsync(tenantId, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

proof.MapGet("/execution-trends", async (
    int? buckets,
    HttpContext ctx,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await proofSvc.GetExecutionTrendsAsync(tenantId, buckets ?? 10, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

proof.MapGet("/override-rates", async (
    HttpContext ctx,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await proofSvc.GetOverrideRatesAsync(tenantId, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

proof.MapGet("/trust-analytics", async (
    HttpContext ctx,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await proofSvc.GetTrustAnalyticsAsync(tenantId, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

proof.MapGet("/dashboard", async (
    string? domain,
    HttpContext ctx,
    IProofAnalyticsService proofSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var dashboard = await proofSvc.GetDashboardAsync(tenantId, domain, ct);
    return Results.Ok(dashboard);
}).RequireAuthorization("GovernanceRead");

// ── Action Safety & Rollback ──────────────────────────────
var actionSafety = v1.MapGroup("/action-safety")
    .WithTags("action-safety");

actionSafety.MapGet("/classifications", async (
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var list = await safetySvc.ListClassificationsAsync(ct);
    return Results.Ok(list);
}).RequireAuthorization("GovernanceRead");

actionSafety.MapGet("/classifications/{actionType}", async (
    string actionType,
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var classification = await safetySvc.GetClassificationAsync(actionType, ct);
    return Results.Ok(classification);
}).RequireAuthorization("GovernanceRead");

actionSafety.MapPut("/classifications", async (
    SetSafetyClassificationRequest req,
    HttpContext ctx,
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";

    if (!Enum.TryParse<ReversibilityLevel>(req.Reversibility, true, out var reversibility))
        return Results.BadRequest(new { error = $"Invalid reversibility: {req.Reversibility}." });
    if (!Enum.TryParse<RollbackStrategy>(req.RollbackStrategy, true, out var strategy))
        return Results.BadRequest(new { error = $"Invalid rollback strategy: {req.RollbackStrategy}." });

    TimeSpan? window = req.RollbackWindowMinutes.HasValue
        ? TimeSpan.FromMinutes(req.RollbackWindowMinutes.Value)
        : null;

    var classification = new ActionSafetyClassification(
        Id: Guid.NewGuid(),
        ActionType: req.ActionType,
        Reversibility: reversibility,
        RollbackSupported: req.RollbackSupported,
        RollbackStrategy: strategy,
        RollbackWindow: window,
        CompensationDescription: req.CompensationDescription,
        OperatorNotes: req.OperatorNotes,
        ClassifiedBy: userId,
        ClassifiedAtUtc: DateTimeOffset.UtcNow);

    var result = await safetySvc.SetClassificationAsync(classification, ct);
    return Results.Ok(result);
}).RequireAuthorization("OperatorOrAdmin");

actionSafety.MapPost("/actions", async (
    RecordGovernedActionRequest req,
    HttpContext ctx,
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var classification = await safetySvc.GetClassificationAsync(req.ActionType, ct);

    var action = new GovernedActionRecord(
        Id: Guid.NewGuid(),
        TenantId: tenantId,
        DecisionId: req.DecisionId,
        WorkflowId: req.WorkflowId,
        ApprovalGateId: req.ApprovalGateId,
        ActionType: req.ActionType,
        Description: req.Description,
        SafetyClassification: classification,
        Status: GovernedActionStatus.Executed,
        ExecutedBy: userId,
        ExecutedAtUtc: DateTimeOffset.UtcNow,
        RollbackHistory: Array.Empty<RollbackAttempt>(),
        CompensationOutcome: null,
        UpdatedAtUtc: DateTimeOffset.UtcNow);

    var created = await safetySvc.RecordActionAsync(action, ct);
    return Results.Created($"/api/v1/action-safety/actions/{created.Id}", created);
}).RequireAuthorization("OperatorOrAdmin");

actionSafety.MapGet("/actions", async (
    int? limit,
    HttpContext ctx,
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var actions = await safetySvc.ListActionsAsync(tenantId, limit ?? 50, ct);
    return Results.Ok(actions);
}).RequireAuthorization("GovernanceRead");

actionSafety.MapGet("/actions/{actionId:guid}", async (
    Guid actionId,
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var action = await safetySvc.GetActionAsync(actionId, ct);
    return action is null ? Results.NotFound() : Results.Ok(action);
}).RequireAuthorization("GovernanceRead");

actionSafety.MapPost("/actions/{actionId:guid}/rollback", async (
    Guid actionId,
    HttpContext ctx,
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
    try
    {
        var result = await safetySvc.AttemptRollbackAsync(actionId, userId, ct);
        return Results.Ok(result);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Governed action not found." });
    }
}).RequireAuthorization("OperatorOrAdmin");

actionSafety.MapGet("/summary", async (
    HttpContext ctx,
    IActionSafetyService safetySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await safetySvc.GetRollbackSummaryAsync(tenantId, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

// ── Enterprise Memory Hierarchy ───────────────────────────
var enterpriseMemory = v1.MapGroup("/enterprise-memory")
    .WithTags("enterprise-memory");

enterpriseMemory.MapPost("/", async (
    StoreEnterpriseMemoryRequest req,
    HttpContext ctx,
    IEnterpriseMemoryService memorySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    if (!Enum.TryParse<MemoryLayer>(req.Layer, true, out var layer))
        return Results.BadRequest(new { error = $"Invalid layer: {req.Layer}." });

    var links = req.LinkedEntities?.Select(e =>
        new MemoryEntityLink(e.EntityType, e.EntityId, e.Relationship)).ToList()
        ?? new List<MemoryEntityLink>();

    var record = new EnterpriseMemoryRecord(
        Id: Guid.NewGuid(),
        TenantId: tenantId,
        Layer: layer,
        Category: req.Category ?? "",
        Subject: req.Subject,
        Content: req.Content,
        Metadata: req.Metadata?.AsReadOnly() ?? new Dictionary<string, string>().AsReadOnly(),
        LinkedEntities: links,
        Tags: req.Tags ?? Array.Empty<string>(),
        Importance: req.Importance ?? 0.5,
        CreatedBy: userId,
        CreatedAtUtc: DateTimeOffset.UtcNow,
        ExpiresAtUtc: req.ExpiresAtUtc);

    var created = await memorySvc.StoreAsync(record, ct);
    return Results.Created($"/api/v1/enterprise-memory/{created.Id}", created);
}).RequireAuthorization("OperatorOrAdmin");

enterpriseMemory.MapGet("/{recordId:guid}", async (
    Guid recordId,
    HttpContext ctx,
    IEnterpriseMemoryService memorySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var record = await memorySvc.GetAsync(recordId, tenantId, ct);
    return record is null ? Results.NotFound() : Results.Ok(record);
}).RequireAuthorization("GovernanceRead");

enterpriseMemory.MapGet("/", async (
    string? layer, string? category, string? tag, int? limit,
    HttpContext ctx,
    IEnterpriseMemoryService memorySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    MemoryLayer? parsedLayer = null;
    if (layer is not null && Enum.TryParse<MemoryLayer>(layer, true, out var l))
        parsedLayer = l;

    var result = await memorySvc.QueryAsync(tenantId, parsedLayer, category, tag, limit ?? 50, ct);
    return Results.Ok(result);
}).RequireAuthorization("GovernanceRead");

enterpriseMemory.MapGet("/entity/{entityType}/{entityId}", async (
    string entityType, string entityId,
    HttpContext ctx,
    IEnterpriseMemoryService memorySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var view = await memorySvc.GetEntityMemoryAsync(tenantId, entityType, entityId, ct);
    return Results.Ok(view);
}).RequireAuthorization("GovernanceRead");

enterpriseMemory.MapGet("/timeline", async (
    string? layer, int? limit,
    HttpContext ctx,
    IEnterpriseMemoryService memorySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    MemoryLayer? parsedLayer = null;
    if (layer is not null && Enum.TryParse<MemoryLayer>(layer, true, out var l))
        parsedLayer = l;

    var timeline = await memorySvc.GetTimelineAsync(tenantId, parsedLayer, limit ?? 100, ct);
    return Results.Ok(timeline);
}).RequireAuthorization("GovernanceRead");

enterpriseMemory.MapDelete("/{recordId:guid}", async (
    Guid recordId,
    HttpContext ctx,
    IEnterpriseMemoryService memorySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var deleted = await memorySvc.DeleteAsync(recordId, tenantId, ct);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("GovernanceWrite");

enterpriseMemory.MapPost("/expire-sessions", async (
    int? maxAgeMinutes,
    HttpContext ctx,
    IEnterpriseMemoryService memorySvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var maxAge = TimeSpan.FromMinutes(maxAgeMinutes ?? 240);
    var expired = await memorySvc.ExpireSessionMemoryAsync(tenantId, maxAge, ct);
    return Results.Ok(new { expiredCount = expired });
}).RequireAuthorization("GovernanceWrite");

// ── Operational Twin ──────────────────────────────────────
var twin = v1.MapGroup("/twin")
    .WithTags("operational-twin");

twin.MapPost("/entities", async (
    UpsertTwinEntityRequest req,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });
    if (!Enum.TryParse<TwinEntityType>(req.EntityType, true, out var entityType))
        return Results.BadRequest(new { error = $"Invalid entity type: {req.EntityType}." });

    var now = DateTimeOffset.UtcNow;
    var entity = new TwinEntity(
        Id: req.Id ?? Guid.NewGuid(), TenantId: tenantId, EntityType: entityType,
        Name: req.Name, Description: req.Description,
        Status: Enum.TryParse<TwinEntityStatus>(req.Status, true, out var s) ? s : TwinEntityStatus.Active,
        Properties: req.Properties?.AsReadOnly() ?? new Dictionary<string, string>().AsReadOnly(),
        Tags: req.Tags ?? Array.Empty<string>(),
        CreatedBy: userId, CreatedAtUtc: now, UpdatedAtUtc: now);

    var created = await twinSvc.UpsertEntityAsync(entity, ct);
    return Results.Created($"/api/v1/twin/entities/{created.Id}", created);
}).RequireAuthorization("OperatorOrAdmin");

twin.MapGet("/entities", async (
    string? type,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    TwinEntityType? parsedType = null;
    if (type is not null && Enum.TryParse<TwinEntityType>(type, true, out var t))
        parsedType = t;

    var entities = await twinSvc.ListEntitiesAsync(tenantId, parsedType, ct);
    return Results.Ok(entities);
}).RequireAuthorization("GovernanceRead");

twin.MapGet("/entities/{entityId:guid}", async (
    Guid entityId,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var entity = await twinSvc.GetEntityAsync(entityId, tenantId, ct);
    return entity is null ? Results.NotFound() : Results.Ok(entity);
}).RequireAuthorization("GovernanceRead");

twin.MapPost("/dependencies", async (
    AddTwinDependencyRequest req,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });
    if (!Enum.TryParse<DependencyType>(req.Type, true, out var depType))
        return Results.BadRequest(new { error = $"Invalid dependency type: {req.Type}." });

    var dep = new TwinDependency(Guid.NewGuid(), tenantId,
        req.FromEntityId, req.ToEntityId, depType,
        req.Label, req.CriticalityScore, DateTimeOffset.UtcNow);
    var created = await twinSvc.AddDependencyAsync(dep, ct);
    return Results.Created($"/api/v1/twin/dependencies/{created.Id}", created);
}).RequireAuthorization("OperatorOrAdmin");

twin.MapGet("/entities/{entityId:guid}/dependencies", async (
    Guid entityId,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var deps = await twinSvc.GetDependenciesAsync(entityId, tenantId, ct);
    return Results.Ok(deps);
}).RequireAuthorization("GovernanceRead");

twin.MapPost("/kpis", async (
    RecordTwinKpiRequest req,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    if (!Enum.TryParse<KpiDirection>(req.Direction, true, out var dir))
        dir = KpiDirection.HigherIsBetter;

    var kpi = new TwinKpi(req.EntityId, req.MetricName, req.CurrentValue,
        req.TargetValue, req.ThresholdWarning, req.ThresholdCritical,
        dir, req.Unit ?? "", DateTimeOffset.UtcNow);
    var created = await twinSvc.RecordKpiAsync(kpi, ct);
    return Results.Ok(created);
}).RequireAuthorization("OperatorOrAdmin");

twin.MapGet("/entities/{entityId:guid}/kpis", async (
    Guid entityId,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var kpis = await twinSvc.GetKpisAsync(entityId, ct);
    return Results.Ok(kpis);
}).RequireAuthorization("GovernanceRead");

twin.MapPost("/bottlenecks", async (
    ReportBottleneckRequest req,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });
    if (!Enum.TryParse<BottleneckSeverity>(req.Severity, true, out var sev))
        sev = BottleneckSeverity.Medium;

    var bn = new TwinBottleneck(Guid.NewGuid(), tenantId, req.AffectedEntityId,
        req.Description, sev, req.RootCause, false, DateTimeOffset.UtcNow, null);
    var created = await twinSvc.ReportBottleneckAsync(bn, ct);
    return Results.Created($"/api/v1/twin/bottlenecks/{created.Id}", created);
}).RequireAuthorization("OperatorOrAdmin");

twin.MapGet("/bottlenecks", async (
    bool? activeOnly,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var bns = await twinSvc.ListBottlenecksAsync(tenantId, activeOnly ?? true, ct);
    return Results.Ok(bns);
}).RequireAuthorization("GovernanceRead");

twin.MapPost("/bottlenecks/{bottleneckId:guid}/resolve", async (
    Guid bottleneckId,
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var resolved = await twinSvc.ResolveBottleneckAsync(bottleneckId, tenantId, ct);
    return resolved is null ? Results.NotFound() : Results.Ok(resolved);
}).RequireAuthorization("OperatorOrAdmin");

twin.MapPost("/entities/{entityId:guid}/links", async (
    Guid entityId,
    LinkTwinArtifactRequest req,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var link = new TwinArtifactLink(Guid.NewGuid(), entityId,
        req.ArtifactType, req.ArtifactId, req.Relationship, DateTimeOffset.UtcNow);
    var created = await twinSvc.LinkArtifactAsync(link, ct);
    return Results.Ok(created);
}).RequireAuthorization("OperatorOrAdmin");

twin.MapGet("/entities/{entityId:guid}/links", async (
    Guid entityId,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var links = await twinSvc.GetArtifactLinksAsync(entityId, ct);
    return Results.Ok(links);
}).RequireAuthorization("GovernanceRead");

twin.MapGet("/overview", async (
    HttpContext ctx,
    IOperationalTwinService twinSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var overview = await twinSvc.GetOverviewAsync(tenantId, ct);
    return Results.Ok(overview);
}).RequireAuthorization("GovernanceRead");

// ══════════════════════════════════════════════════════════════
//  Scenario Engine
// ══════════════════════════════════════════════════════════════
var scenarios = v1.MapGroup("/scenarios")
    .WithTags("Scenarios")
    .RequireRateLimiting("api");

scenarios.MapPost("/", async (
    CreateScenarioRequest req,
    HttpContext ctx,
    IScenarioService scenarioSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    if (!Enum.TryParse<ScenarioType>(req.Type, true, out var scenarioType))
        return Results.BadRequest(new { error = $"Invalid scenario type: {req.Type}" });

    var assumptions = req.Assumptions?.Select(a =>
        new ScenarioAssumption(a.Name, a.CurrentValue, a.ProposedValue, a.Unit, a.Rationale)).ToList()
        ?? new List<ScenarioAssumption>();

    var linkedKpis = req.LinkedKpiIds?.Select(id =>
        new ScenarioLink("Kpi", id, null)).ToList()
        ?? new List<ScenarioLink>();

    var linkedDecisions = req.LinkedDecisionIds?.Select(id =>
        new ScenarioLink("Decision", id, null)).ToList()
        ?? new List<ScenarioLink>();

    var linkedEntities = req.LinkedEntityIds?.Select(id =>
        new ScenarioLink("TwinEntity", id, null)).ToList()
        ?? new List<ScenarioLink>();

    var now = DateTimeOffset.UtcNow;
    var user = ctx.User?.Identity?.Name ?? "system";

    var scenario = new Scenario(
        Id: Guid.NewGuid(),
        TenantId: tenantId,
        Title: req.Title,
        Description: req.Description,
        Type: scenarioType,
        Status: ScenarioStatus.Draft,
        Assumptions: assumptions,
        ProjectedEffects: Array.Empty<ProjectedEffect>(),
        LinkedKpis: linkedKpis,
        LinkedDecisions: linkedDecisions,
        LinkedEntities: linkedEntities,
        CreatedBy: user,
        CreatedAtUtc: now,
        UpdatedAtUtc: now);

    // Derive initial projected effects from assumptions
    if (assumptions.Count > 0)
    {
        var effects = ArchonAI.Api.Security.ScenarioService.DeriveProjectedEffects(scenario, assumptions);
        scenario = scenario with { ProjectedEffects = effects, Status = ScenarioStatus.Active };
    }

    var result = await scenarioSvc.CreateScenarioAsync(scenario, ct);
    return Results.Created($"/api/v1/scenarios/{result.Id}", result);
}).RequireAuthorization("GovernanceWrite");

scenarios.MapPut("/{scenarioId:guid}/assumptions", async (
    Guid scenarioId,
    UpdateAssumptionsRequest req,
    HttpContext ctx,
    IScenarioService scenarioSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var assumptions = req.Assumptions.Select(a =>
        new ScenarioAssumption(a.Name, a.CurrentValue, a.ProposedValue, a.Unit, a.Rationale)).ToList();

    var updated = await scenarioSvc.UpdateAssumptionsAsync(scenarioId, tenantId, assumptions, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).RequireAuthorization("GovernanceWrite");

scenarios.MapGet("/{scenarioId:guid}", async (
    Guid scenarioId,
    HttpContext ctx,
    IScenarioService scenarioSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var scenario = await scenarioSvc.GetScenarioAsync(scenarioId, tenantId, ct);
    return scenario is null ? Results.NotFound() : Results.Ok(scenario);
}).RequireAuthorization("GovernanceRead");

scenarios.MapGet("/", async (
    HttpContext ctx,
    IScenarioService scenarioSvc,
    string? type,
    string? status,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    ScenarioType? typeFilter = null;
    if (type is not null && Enum.TryParse<ScenarioType>(type, true, out var st))
        typeFilter = st;

    ScenarioStatus? statusFilter = null;
    if (status is not null && Enum.TryParse<ScenarioStatus>(status, true, out var ss))
        statusFilter = ss;

    var list = await scenarioSvc.ListScenariosAsync(tenantId, typeFilter, statusFilter, ct);
    return Results.Ok(list);
}).RequireAuthorization("GovernanceRead");

scenarios.MapPost("/compare", async (
    CompareScenarioRequest req,
    HttpContext ctx,
    IScenarioService scenarioSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    if (req.ScenarioIds.Count < 2)
        return Results.BadRequest(new { error = "At least 2 scenario IDs required for comparison." });

    var comparison = await scenarioSvc.CompareScenariosAsync(req.ScenarioIds, tenantId, ct);
    return Results.Ok(comparison);
}).RequireAuthorization("GovernanceRead");

scenarios.MapDelete("/{scenarioId:guid}", async (
    Guid scenarioId,
    HttpContext ctx,
    IScenarioService scenarioSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var deleted = await scenarioSvc.DeleteScenarioAsync(scenarioId, tenantId, ct);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("GovernanceWrite");

// ══════════════════════════════════════════════════════════════
//  Exception Intelligence
// ══════════════════════════════════════════════════════════════
var exceptions = v1.MapGroup("/exceptions")
    .WithTags("Exceptions")
    .RequireRateLimiting("api");

exceptions.MapPost("/", async (
    RaiseExceptionRequest req,
    HttpContext ctx,
    IExceptionIntelligenceService exSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    if (!Enum.TryParse<ExceptionCategory>(req.Category, true, out var category))
        return Results.BadRequest(new { error = $"Invalid category: {req.Category}" });
    if (!Enum.TryParse<ExceptionSeverity>(req.Severity, true, out var severity))
        return Results.BadRequest(new { error = $"Invalid severity: {req.Severity}" });

    var escalation = EscalationLevel.None;
    if (req.EscalationLevel is not null)
        Enum.TryParse(req.EscalationLevel, true, out escalation);

    var links = req.LinkedArtifacts?.Select(l =>
        new ExceptionArtifactLink(l.ArtifactType, l.ArtifactId, l.Label)).ToList()
        ?? new List<ExceptionArtifactLink>();

    RecommendedAction? action = null;
    if (req.RecommendedAction is not null)
    {
        var ra = req.RecommendedAction;
        action = new RecommendedAction(ra.ActionType, ra.Description, ra.TargetArtifactType, ra.TargetArtifactId, ra.Confidence ?? "Medium");
    }

    var now = DateTimeOffset.UtcNow;
    var user = ctx.User?.Identity?.Name ?? "system";

    var exception = new OperationalException(
        Id: Guid.NewGuid(),
        TenantId: tenantId,
        Category: category,
        Severity: severity,
        Title: req.Title,
        Description: req.Description ?? "",
        Domain: req.Domain ?? "General",
        Status: ExceptionStatus.Open,
        Urgency: req.Urgency ?? 0.5,
        EconomicImpactEstimate: req.EconomicImpactEstimate ?? 0,
        Confidence: req.Confidence ?? 0.5,
        EscalationLevel: escalation,
        AssignedTo: req.AssignedTo,
        EscalationPath: req.EscalationPath,
        LinkedArtifacts: links,
        RecommendedAction: action,
        CreatedBy: user,
        CreatedAtUtc: now,
        UpdatedAtUtc: now,
        AcknowledgedAtUtc: null,
        ResolvedAtUtc: null);

    var result = await exSvc.RaiseExceptionAsync(exception, ct);
    return Results.Created($"/api/v1/exceptions/{result.Id}", result);
}).RequireAuthorization("GovernanceWrite");

exceptions.MapGet("/", async (
    HttpContext ctx,
    IExceptionIntelligenceService exSvc,
    string? severity, string? category, string? status, string? domain,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    ExceptionSeverity? sevFilter = null;
    if (severity is not null && Enum.TryParse<ExceptionSeverity>(severity, true, out var sv)) sevFilter = sv;
    ExceptionCategory? catFilter = null;
    if (category is not null && Enum.TryParse<ExceptionCategory>(category, true, out var cv)) catFilter = cv;
    ExceptionStatus? statusFilter = null;
    if (status is not null && Enum.TryParse<ExceptionStatus>(status, true, out var stv)) statusFilter = stv;

    var list = await exSvc.ListExceptionsAsync(tenantId, sevFilter, catFilter, statusFilter, domain, ct);
    return Results.Ok(list);
}).RequireAuthorization("GovernanceRead");

exceptions.MapGet("/{exceptionId:guid}", async (
    Guid exceptionId,
    HttpContext ctx,
    IExceptionIntelligenceService exSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var ex = await exSvc.GetExceptionAsync(exceptionId, tenantId, ct);
    return ex is null ? Results.NotFound() : Results.Ok(ex);
}).RequireAuthorization("GovernanceRead");

exceptions.MapPut("/{exceptionId:guid}/status", async (
    Guid exceptionId,
    UpdateExceptionStatusRequest req,
    HttpContext ctx,
    IExceptionIntelligenceService exSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    if (!Enum.TryParse<ExceptionStatus>(req.Status, true, out var status))
        return Results.BadRequest(new { error = $"Invalid status: {req.Status}" });

    var updated = await exSvc.UpdateStatusAsync(exceptionId, tenantId, status, req.AssignedTo, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).RequireAuthorization("GovernanceWrite");

exceptions.MapPost("/{exceptionId:guid}/recommended-action", async (
    Guid exceptionId,
    SetRecommendedActionRequest req,
    HttpContext ctx,
    IExceptionIntelligenceService exSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var action = new RecommendedAction(
        req.ActionType, req.Description,
        req.TargetArtifactType, req.TargetArtifactId,
        req.Confidence ?? "Medium");

    var updated = await exSvc.SetRecommendedActionAsync(exceptionId, tenantId, action, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).RequireAuthorization("GovernanceWrite");

exceptions.MapGet("/summary", async (
    HttpContext ctx,
    IExceptionIntelligenceService exSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await exSvc.GetQueueSummaryAsync(tenantId, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

exceptions.MapGet("/prioritized", async (
    HttpContext ctx,
    IExceptionIntelligenceService exSvc,
    int? limit,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var queue = await exSvc.GetPrioritizedQueueAsync(tenantId, limit ?? 20, ct);
    return Results.Ok(queue);
}).RequireAuthorization("GovernanceRead");

// ══════════════════════════════════════════════════════════════
//  Executive Command Layer
// ══════════════════════════════════════════════════════════════
var execCmd = v1.MapGroup("/executive-command")
    .WithTags("ExecutiveCommand")
    .RequireRateLimiting("api");

execCmd.MapGet("/summary", async (
    HttpContext ctx,
    IExecutiveCommandService cmdSvc,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var summary = await cmdSvc.GetCommandSummaryAsync(tenantId, ct);
    return Results.Ok(summary);
}).RequireAuthorization("GovernanceRead");

// ── Hero Workflows ──────────────────────────────────────────────
var heroWorkflows = v1.MapGroup("/hero-workflows")
    .WithTags("HeroWorkflows")
    .RequireRateLimiting("api");

heroWorkflows.MapGet("/catalog", async (
    IHeroWorkflowService heroSvc,
    CancellationToken ct) =>
{
    var catalog = await heroSvc.GetCatalogAsync(ct);
    return Results.Ok(catalog);
}).RequireAuthorization("GovernanceRead");

heroWorkflows.MapGet("/catalog/{workflowType}", async (
    string workflowType,
    IHeroWorkflowService heroSvc,
    CancellationToken ct) =>
{
    var def = await heroSvc.GetDefinitionAsync(workflowType, ct);
    return def is null ? Results.NotFound() : Results.Ok(def);
}).RequireAuthorization("GovernanceRead");

heroWorkflows.MapPost("/", async (
    StartHeroWorkflowRequest req,
    IHeroWorkflowService heroSvc,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.WorkflowType) || string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest(new { error = "WorkflowType and Title are required." });

    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    var instance = await heroSvc.StartAsync(
        tenantId, req.WorkflowType, req.Title,
        req.Inputs ?? new Dictionary<string, string>(),
        userId, ct);

    return Results.Created($"/api/v1/hero-workflows/{instance.Id}", instance);
}).RequireAuthorization("GovernanceWrite");

heroWorkflows.MapPost("/{workflowId:guid}/advance", async (
    Guid workflowId,
    AdvanceHeroWorkflowRequest? req,
    IHeroWorkflowService heroSvc,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    var result = await heroSvc.AdvanceAsync(workflowId, tenantId, req?.Inputs, userId, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization("GovernanceWrite");

heroWorkflows.MapGet("/{workflowId:guid}", async (
    Guid workflowId,
    IHeroWorkflowService heroSvc,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;

    var result = await heroSvc.GetAsync(workflowId, tenantId, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization("GovernanceRead");

heroWorkflows.MapGet("/", async (
    IHeroWorkflowService heroSvc,
    HttpContext httpContext,
    string? workflowType,
    string? status,
    int? limit,
    CancellationToken ct) =>
{
    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
    var parsedStatus = Enum.TryParse<HeroWorkflowStatus>(status, true, out var s)
        ? s : (HeroWorkflowStatus?)null;

    var results = await heroSvc.ListAsync(tenantId, workflowType, parsedStatus, limit ?? 50, ct);
    return Results.Ok(results);
}).RequireAuthorization("GovernanceRead");

heroWorkflows.MapPost("/{workflowId:guid}/cancel", async (
    Guid workflowId,
    IHeroWorkflowService heroSvc,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    var result = await heroSvc.CancelAsync(workflowId, tenantId, userId, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization("GovernanceWrite");

// ── Policy Simulation / Dry-Run ────────────────────────────────
var policySimulation = v1.MapGroup("/policy-simulation")
    .WithTags("PolicySimulation")
    .RequireRateLimiting("api");

policySimulation.MapPost("/simulate", async (
    RunSimulationRequest req,
    IPolicySimulationService simSvc,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ActionType) || string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest(new { error = "ActionType and Title are required." });

    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    var request = new SimulationRequest(
        TenantId: tenantId,
        ActionType: req.ActionType,
        ActionScope: req.ActionScope ?? req.ActionType,
        Title: req.Title,
        Domain: req.Domain,
        Objective: req.Objective,
        RiskLevel: req.RiskLevel,
        Reversibility: req.Reversibility,
        Confidence: req.Confidence,
        ExpectedValue: req.ExpectedValue,
        RevenueImpactLow: req.RevenueImpactLow,
        RevenueImpactHigh: req.RevenueImpactHigh,
        CostImpactLow: req.CostImpactLow,
        CostImpactHigh: req.CostImpactHigh,
        DownsideRisk: req.DownsideRisk,
        UpsidePotential: req.UpsidePotential,
        RequestedTier: req.RequestedTier,
        WorkflowType: req.WorkflowType,
        RequestedBy: userId);

    var result = await simSvc.SimulateAsync(request, ct);
    return Results.Ok(result);
}).RequireAuthorization("GovernanceRead");

policySimulation.MapGet("/{simulationId:guid}", async (
    Guid simulationId,
    IPolicySimulationService simSvc,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;

    var result = await simSvc.GetAsync(simulationId, tenantId, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization("GovernanceRead");

policySimulation.MapGet("/", async (
    IPolicySimulationService simSvc,
    HttpContext httpContext,
    int? limit,
    CancellationToken ct) =>
{
    var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
    var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;

    var results = await simSvc.ListAsync(tenantId, limit ?? 50, ct);
    return Results.Ok(results);
}).RequireAuthorization("GovernanceRead");

// ── Operator Inspection & Diagnostics ─────────────────────────────
var inspection = v1.MapGroup("/inspection")
    .WithTags("Inspection")
    .RequireRateLimiting("api");

inspection.MapGet("/summaries", async (
    IInspectionService inspectionSvc,
    HttpContext ctx,
    string? subjectType,
    string? domain,
    int? limit,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var results = await inspectionSvc.ListInspectionSummariesAsync(
        tenantId, subjectType, domain, limit ?? 50, ct);
    return Results.Ok(results);
}).RequireAuthorization("GovernanceRead");

inspection.MapGet("/decisions/{decisionId:guid}/rationale", async (
    Guid decisionId,
    IInspectionService inspectionSvc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var bundle = await inspectionSvc.InspectDecisionRationaleAsync(decisionId, tenantId, ct);
    return bundle is null ? Results.NotFound() : Results.Ok(bundle);
}).RequireAuthorization("GovernanceRead");

inspection.MapGet("/policy/{subjectType}/{subjectId}", async (
    string subjectType,
    string subjectId,
    IInspectionService inspectionSvc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var result = await inspectionSvc.InspectPolicyEvaluationAsync(subjectType, subjectId, tenantId, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization("GovernanceRead");

inspection.MapGet("/memory/{subjectType}/{subjectId}", async (
    string subjectType,
    string subjectId,
    IInspectionService inspectionSvc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var refs = await inspectionSvc.InspectMemoryReferencesAsync(subjectType, subjectId, tenantId, ct);
    return Results.Ok(refs);
}).RequireAuthorization("GovernanceRead");

inspection.MapGet("/workflows/{workflowId:guid}/diagnostics", async (
    Guid workflowId,
    IInspectionService inspectionSvc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
    if (tenantClaim is null) return Results.Unauthorized();
    if (!Guid.TryParse(tenantClaim, out var tenantId))
        return Results.BadRequest(new { error = "Invalid tenant_id." });

    var diag = await inspectionSvc.InspectWorkflowFailureAsync(workflowId, tenantId, ct);
    return diag is null ? Results.NotFound() : Results.Ok(diag);
}).RequireAuthorization("GovernanceRead");

app.Run();


public sealed record QuickBooksInvoiceRequest(string CustomerId, IReadOnlyList<QuickBooksLineItem> LineItems);
public sealed record SlackSendMessageRequest(string Channel, string Text, string? ThreadTs = null);
public sealed record SlackAlertRequest(string Channel, string AlertLevel, string Title, string Details);
public sealed record GmailSendRequest(string To, string Subject, string Body, bool IsHtml = false);
public sealed record GoogleDocCreateRequest(string Title, string? Content = null);
public sealed record GoogleSheetWriteRequest(string Range, IReadOnlyList<IReadOnlyList<string>> Values);
public sealed record M365SendEmailRequest(string To, string Subject, string Body, bool IsHtml = false);
public sealed record M365TeamsMessageRequest(string Content);
public sealed record WorkflowCoordinationRequest(string WorkflowTemplate, IReadOnlyList<string> AgentCapabilities, Dictionary<string, string> Inputs);
public sealed record FinanceAnalysisRequest(string Scope, Dictionary<string, string> Parameters);
public sealed record FinanceSummaryRequest(string Scope, string Period);
public sealed record BudgetAssistRequest(string DepartmentId, Dictionary<string, string> Parameters);
public sealed record SupportAnalysisRequest(string Scope, Dictionary<string, string> Parameters);
public sealed record AgentEnabledRequest(bool Enabled);
public sealed record CreateRoleRequest(string Name, string Description, IReadOnlyList<string> Permissions);
public sealed record UpdateRoleRequest(string Description, IReadOnlyList<string> Permissions);
public sealed record AssignRoleRequest(string SubjectId, string SubjectType, Guid RoleId, string AssignedBy);
public sealed record CreatePolicyRequest(string Name, string Description, IReadOnlyList<string> RequiredPermissions, string Resource, string Effect, Dictionary<string, string> Conditions);
public sealed record UpdatePolicyEnabledRequest(bool IsEnabled);
public sealed record EvaluateAccessRequest(string SubjectId, string Resource, string Action);
public sealed record AuditRecordRequest(string EventType, string Category, string Source, string SubjectId, string SubjectType, string Action, string ResourceType, string ResourceId, string Description, Dictionary<string, string>? Metadata = null);
public sealed record AuditVerifyRequest(Guid? FromEntryId = null);
public sealed record CreateWorkflowStepRequest(int Order, string Name, string Description, string AgentType, Dictionary<string, string> Inputs);
public sealed record CreateWorkflowRequest(string Name, string Description, string Strategy, IReadOnlyList<CreateWorkflowStepRequest> Steps, Dictionary<string, string>? Metadata = null);
public sealed record ExecuteWorkflowRequest(string TenantId, Dictionary<string, string>? Metadata = null);
public sealed record ProvisionTenantRequest(string Name, string DisplayName, TenantTier Tier, Dictionary<string, string>? Metadata = null);
public sealed record SuspendTenantRequest(string Reason);
public sealed record RegisterWorkflowRequest(string TenantId, string Name, string Description, string Strategy, int StepCount, Dictionary<string, string>? Metadata = null);
public sealed record UpdateManagedStatusRequest(ManagedWorkflowStatus Status);
public sealed record RegisterManagedAgentRequest(string TenantId, string Name, string Version, IReadOnlyList<string> Capabilities, Dictionary<string, string>? Configuration = null);
public sealed record UpdateManagedAgentStatusRequest(ManagedAgentStatus Status);
public sealed record CreatePlatformPolicyRequest(string TenantId, string Name, string Description, PlatformPolicyType PolicyType, string TargetResource, Dictionary<string, string> Rules, int Priority);
public sealed record UpdatePlatformPolicyRequest(bool IsEnabled, Dictionary<string, string>? Rules = null, int? Priority = null);
public sealed record SetConfigurationRequest(string TenantId, string Scope, string Key, string Value, string? Description = null, bool IsSecret = false);
public sealed record CreateWorkflowNodeRequest(string Name, string Description, WorkflowNodeType NodeType, Dictionary<string, string> Configuration);
public sealed record CreateWorkflowEdgeRequest(int SourceNodeIndex, int TargetNodeIndex, string? Label = null);
public sealed record CreateWorkflowGraphRequest(string Name, string Description, IReadOnlyList<CreateWorkflowNodeRequest> Nodes, IReadOnlyList<CreateWorkflowEdgeRequest> Edges, Dictionary<string, string>? Metadata = null);
public sealed record AgentCapabilityInput(string Name, string Description, string Category, string Version);
public sealed record RegisterAgentRequest(string Name, string Description, string Version, IReadOnlyList<AgentCapabilityInput> Capabilities, Dictionary<string, string>? Configuration = null);
public sealed record UpdateAgentCapabilitiesRequest(IReadOnlyList<AgentCapabilityInput> Capabilities);
public sealed record DisableAgentRequest(string Reason);
public sealed record RecordAgentMetricsRequest(long TotalExecutions, long SuccessfulExecutions, long FailedExecutions, double AverageLatencyMs, double P95LatencyMs, double UptimePercent);
public sealed record ResourceUsageInput(double EstimatedCpuSeconds, double EstimatedMemoryMb, int EstimatedAgentCount, double EstimatedCostPerExecution, string CostCurrency);
public sealed record CreateStrategyTemplateRequest(string Name, string Description, string ObjectiveType, string WorkflowTemplate, Dictionary<string, string> SuccessMetrics, ResourceUsageInput ResourceUsage, List<string>? Tags = null, Dictionary<string, string>? Metadata = null);
public sealed record UpdateStrategyTemplateRequest(string? Description = null, string? WorkflowTemplate = null, Dictionary<string, string>? SuccessMetrics = null, ResourceUsageInput? ResourceUsage = null, List<string>? Tags = null);
public sealed record RecordStrategyExecutionRequest(bool IsSuccess, double LatencyMs, double Cost, Dictionary<string, string>? Outcomes = null);
public sealed record CompareStrategiesRequest(IReadOnlyList<Guid> StrategyIds);
public sealed record SimulateWorkflowRequest(Guid WorkflowGraphId, Guid? StrategyId = null, Dictionary<string, string>? HistoricalOverrides = null);
public sealed record RecordHistoricalExecutionRequest(Guid WorkflowGraphId, bool IsSuccess, double LatencyMs, double Cost);
public sealed record RequestTaskSupportInput(Guid RequestingAgentId, string RequestingAgentName, Guid TaskId, string RequiredCapability, string Reason, Dictionary<string, string>? Context = null, int? TimeoutSeconds = null);
public sealed record ShareKnowledgeInput(Guid SourceAgentId, string SourceAgentName, Guid? TargetAgentId, string Topic, string Content, Dictionary<string, string>? Metadata = null);
public sealed record DelegateTaskInput(Guid DelegatingAgentId, string DelegatingAgentName, Guid TargetAgentId, string TargetAgentName, Guid OriginalTaskId, string RequiredCapability, Dictionary<string, string>? TaskInputs = null, int? TimeoutSeconds = null);
public sealed record OrgMemorySearchRequest(string QueryText, string? FilterType = null, string? FilterCategory = null, int? TopK = null);
public sealed record CompressMemoryRequest(string Scope);
public sealed record DeduplicateMemoryRequest(string Scope);
public sealed record ClusterMemoryRequest(string Scope);
public sealed record SummarizeMemoryRequest(string Scope, IReadOnlyList<Guid> SourceRecordIds);
public sealed record SearchMemoryRequest(string Scope, IReadOnlyList<float> QueryEmbedding, int? TopK = null);
public sealed record RebuildIndexRequest(string Scope);
public sealed record AddSecurityPolicyRequest(string Name, string Category, string RuleType, IReadOnlyList<string>? AllowedValues = null, IReadOnlyList<string>? DeniedValues = null, Dictionary<string, string>? Limits = null);
public sealed record EvaluateDataAccessRequest(string SubjectId, string ResourceType, string Action);
public sealed record EvaluateWorkflowLimitsRequest(Guid WorkflowId, int StepCount, int ConcurrentAgents);
public sealed record RecordAgentLoadRequest(Guid AgentId, string AgentName, int ActiveTasks, int QueuedTasks, double ExecutionTimeMs, double CpuPercent, double MemoryPercent);
public sealed record RecordModelLatencyRequest(string Provider, string Model, double LatencyMs, bool Success);
public sealed record RegisterClusterNodeRequest(string HostName, string Role, int MaxConcurrentTasks, int MaxAgents, double CpuCores, long MemoryBytes, int GpuSlots, IReadOnlyList<string> Capabilities, Dictionary<string, string>? Labels = null);
public sealed record NodeHeartbeatRequest(int ActiveTasks, int QueuedTasks, int ActiveAgents, double CpuUtilizationPercent, double MemoryUtilizationPercent, int GpuSlotsUsed);
public sealed record SystemPauseRequest(string Reason);
public sealed record RaiseAlertRequest(string Severity, string Component, string Message);
public sealed record IngestSignalRequest(string SignalType, string SourceSystem, string EntityId, DateTimeOffset? Timestamp, Dictionary<string, string> Payload);
public sealed record IngestSignalBatchRequest(IReadOnlyList<IngestSignalRequest> Signals);
public sealed record CancelGoalRequest(string Reason);

public sealed record EconomicEvaluationRequest(
    string ObjectiveTitle,
    string ObjectiveDescription,
    IReadOnlyList<string>? CandidateStrategies,
    Dictionary<string, string>? Constraints,
    DateTimeOffset? Deadline,
    EconomicWeightsDto? Weights);

public sealed record SingleStrategyEvaluationRequest(
    string ObjectiveTitle,
    string ObjectiveDescription,
    string Strategy,
    Dictionary<string, string>? Constraints,
    DateTimeOffset? Deadline);

public sealed record EconomicWeightsDto(
    double CostWeight,
    double ImpactWeight,
    double SuccessProbabilityWeight,
    double ExecutionTimeWeight);

public sealed record BuildTaskGraphRequest(
    Guid GoalId,
    string? Strategy);

public sealed record RegisterTaskTypesRequest(
    IReadOnlyList<string> TaskTypes);

public sealed record CreateCollaborationSessionInput(
    Guid InitiatorAgentId,
    string InitiatorAgentName,
    string Purpose,
    Dictionary<string, string>? InitialContext);

public sealed record JoinCollaborationSessionInput(
    Guid AgentId,
    string AgentName,
    string Role);

public sealed record ShareCollaborationContextInput(
    Guid AgentId,
    Dictionary<string, string> Context);

public sealed record SmartDelegationInput(
    Guid DelegatingAgentId,
    string DelegatingAgentName,
    string RequiredCapability,
    string? PreferredTaskType,
    Dictionary<string, string>? TaskInputs,
    IReadOnlyList<Guid>? FallbackAgentIds,
    int? TimeoutSeconds);

public sealed record AssistanceRequestInput(
    Guid RequestingAgentId,
    string RequestingAgentName,
    string Objective,
    IReadOnlyList<string> RequiredCapabilities,
    Dictionary<string, string>? Context,
    int? MaxResponders,
    int? TimeoutSeconds);

public sealed record SimulateStrategyRequest(
    Guid GraphId,
    string Strategy);

public sealed record CompareTaskGraphStrategiesRequest(
    Guid GraphId,
    IReadOnlyList<string> Strategies);

public sealed record SimulateGoalStrategiesRequest(
    Guid GoalId,
    IReadOnlyList<string>? Strategies);

public sealed record SimulationGuidedPlanRequest(
    Guid GoalId,
    IReadOnlyList<string>? Strategies);

public sealed record EvaluateOutcomeRequest(
    StrategySimulationResult SimulationResult,
    TaskGraphExecutionResult ActualResult);

public sealed record ExplainStrategyRequest(
    Guid GoalId,
    string GoalTitle,
    IReadOnlyList<string> CandidateStrategies);

public sealed record ExplainAgentRequest(
    string RequiredCapability,
    string? TaskType);

public sealed record ExplainDecisionRequest(
    Guid GoalId,
    string GoalTitle,
    IReadOnlyList<string> CandidateStrategies,
    string RequiredCapability,
    string? TaskType);

// ── Auth DTOs ──────────────────────────────────────────────

public sealed record RegisterRequest(
    string OrganizationName,
    string Email,
    string Password,
    string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record InviteRequest(string Email, string? Role);

public sealed record AcceptInviteRequest(
    string InviteToken,
    string Password,
    string? DisplayName);

public sealed record UserInfo(Guid Id, string Email, string DisplayName, string Role);
public sealed record OrgInfo(Guid Id, string Name, string Slug);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    UserInfo User,
    OrgInfo Organization);

public sealed record MeResponse(
    UserInfo User,
    OrgInfo Organization);

// ── Governance DTOs ────────────────────────────────────────

public sealed record CreateApprovalPolicyRequest(
    string ActionType,
    string Description,
    string RequiredApproverRole,
    bool RequireSeparationOfDuties);

public sealed record RequestApprovalRequest(
    string ActionType,
    string ResourceId,
    string Justification);

public sealed record ReviewApprovalRequest(
    bool Approve,
    string? Notes);

// ── Decision DTOs ─────────────────────────────────────────

public sealed record CreateDecisionRequest(
    string Title,
    string Domain,
    string? Objective,
    IReadOnlyList<string>? Constraints,
    IReadOnlyList<string>? Assumptions,
    IReadOnlyList<CreateAlternativeRequest>? Alternatives,
    string? RecommendedOptionId,
    double? Confidence,
    string? Reversibility,
    string? RiskLevel,
    decimal? ExpectedValue,
    bool? RequiresApproval);

public sealed record CreateAlternativeRequest(
    string Title,
    string Rationale,
    IReadOnlyList<string>? Pros,
    IReadOnlyList<string>? Cons,
    double? EstimatedConfidence,
    decimal? EstimatedValue);

public sealed record UpdateDecisionStatusRequest(
    string Status,
    string? Detail);

public sealed record CreateDecisionLinkRequest(
    string ArtifactType,
    string ArtifactId,
    string? Description);

// ── Financial Consequence DTOs ────────────────────────────
public sealed record AttachFinancialConsequenceRequest(
    decimal? ExpectedRevenueImpactLow,
    decimal? ExpectedRevenueImpactHigh,
    decimal? ExpectedCostImpactLow,
    decimal? ExpectedCostImpactHigh,
    decimal? ExpectedMarginImpact,
    string? ExpectedCashTimingImpact,
    string? LaborImpact,
    decimal? DownsideRisk,
    decimal? UpsidePotential,
    double? ConfidenceAdjustment,
    decimal? RoiEstimateLow,
    decimal? RoiEstimateHigh,
    string? BreakEvenEstimate,
    IReadOnlyList<string>? Assumptions,
    string? Notes);

// ── Trust Tier DTOs ──────────────────────────────────────
public sealed record SetTrustTierPolicyRequest(
    Guid? Id,
    string ActionScope,
    string MaxTier,
    double? ConfidenceThreshold,
    decimal? ValueCeiling,
    bool? RequireReversible,
    string? Description,
    bool? IsEnabled);

public sealed record EvaluateTrustTierRequest(
    string ActionScope,
    string RequestedTier,
    double? Confidence,
    decimal? Value,
    bool? Reversible);

// ── Outcome Learning DTOs ────────────────────────────────
public sealed record RecordExpectedOutcomeRequest(
    Guid DecisionId,
    string? ExpectedSummary,
    decimal? ExpectedValue,
    double ConfidenceAtPrediction,
    string? ExpectedTimeframe);

public sealed record RecordActualOutcomeRequest(
    Guid DecisionId,
    string? ActualSummary,
    decimal? ActualValue,
    string? RootCause,
    string? Notes);

// ── Enterprise Memory DTOs ───────────────────────────────
public sealed record StoreEnterpriseMemoryRequest(
    string Layer,
    string Subject,
    string Content,
    string? Category,
    Dictionary<string, string>? Metadata,
    IReadOnlyList<MemoryEntityLinkDto>? LinkedEntities,
    IReadOnlyList<string>? Tags,
    double? Importance,
    DateTimeOffset? ExpiresAtUtc);

public sealed record MemoryEntityLinkDto(
    string EntityType,
    string EntityId,
    string Relationship);

// ── Operational Twin DTOs ────────────────────────────────
public sealed record UpsertTwinEntityRequest(
    Guid? Id, string EntityType, string Name, string? Description,
    string? Status, Dictionary<string, string>? Properties,
    IReadOnlyList<string>? Tags);

public sealed record AddTwinDependencyRequest(
    Guid FromEntityId, Guid ToEntityId, string Type,
    string? Label, double? CriticalityScore);

public sealed record RecordTwinKpiRequest(
    Guid EntityId, string MetricName, double CurrentValue,
    double? TargetValue, double? ThresholdWarning, double? ThresholdCritical,
    string? Direction, string? Unit);

public sealed record ReportBottleneckRequest(
    Guid AffectedEntityId, string Description, string Severity,
    string? RootCause);

public sealed record LinkTwinArtifactRequest(
    string ArtifactType, string ArtifactId, string Relationship);

// ── Scenario Engine DTOs ────────────────────────────────────
public sealed record CreateScenarioRequest(
    string Title, string? Description, string Type,
    IReadOnlyList<AssumptionDto>? Assumptions,
    IReadOnlyList<string>? LinkedKpiIds,
    IReadOnlyList<string>? LinkedDecisionIds,
    IReadOnlyList<string>? LinkedEntityIds);

public sealed record AssumptionDto(
    string Name, string CurrentValue, string ProposedValue,
    string? Unit, string? Rationale);

public sealed record UpdateAssumptionsRequest(
    IReadOnlyList<AssumptionDto> Assumptions);

public sealed record CompareScenarioRequest(
    IReadOnlyList<Guid> ScenarioIds);

// ── Exception Intelligence DTOs ─────────────────────────────
public sealed record RaiseExceptionRequest(
    string Category, string Severity, string Title,
    string? Description, string? Domain,
    double? Urgency, double? EconomicImpactEstimate,
    double? Confidence, string? EscalationLevel,
    string? AssignedTo, string? EscalationPath,
    IReadOnlyList<ExceptionArtifactLinkDto>? LinkedArtifacts,
    RecommendedActionDto? RecommendedAction);

public sealed record ExceptionArtifactLinkDto(
    string ArtifactType, string ArtifactId, string? Label);

public sealed record RecommendedActionDto(
    string ActionType, string Description,
    string? TargetArtifactType, string? TargetArtifactId,
    string? Confidence);

public sealed record UpdateExceptionStatusRequest(
    string Status, string? AssignedTo);

public sealed record SetRecommendedActionRequest(
    string ActionType, string Description,
    string? TargetArtifactType, string? TargetArtifactId,
    string? Confidence);

// ── Hero Workflow DTOs ────────────────────────────────────────
public sealed record StartHeroWorkflowRequest(
    string WorkflowType,
    string Title,
    Dictionary<string, string>? Inputs);

public sealed record AdvanceHeroWorkflowRequest(
    Dictionary<string, string>? Inputs);

// ── Action Safety DTOs ──────────────────────────────────────
public sealed record SetSafetyClassificationRequest(
    string ActionType,
    string Reversibility,
    bool RollbackSupported,
    string RollbackStrategy,
    int? RollbackWindowMinutes,
    string? CompensationDescription,
    string? OperatorNotes);

public sealed record RecordGovernedActionRequest(
    string ActionType,
    string Description,
    Guid? DecisionId,
    Guid? WorkflowId,
    Guid? ApprovalGateId);

// ── Proof Analytics DTOs ────────────────────────────────────
public sealed record RecordProofEventRequest(
    Guid DecisionId,
    Guid? WorkflowId,
    string EventType,
    string? Detail,
    decimal? ExpectedValue,
    decimal? ActualValue,
    decimal? Variance,
    double? VariancePercent,
    string? ActionType,
    bool? IsSuccess,
    string? OverrideReason,
    decimal? EconomicImpact,
    string? ImpactAttribution);

// ── Policy Simulation DTOs ──────────────────────────────────
public sealed record RunSimulationRequest(
    string ActionType,
    string? ActionScope,
    string Title,
    string? Domain,
    string? Objective,
    string? RiskLevel,
    string? Reversibility,
    double? Confidence,
    decimal? ExpectedValue,
    decimal? RevenueImpactLow,
    decimal? RevenueImpactHigh,
    decimal? CostImpactLow,
    decimal? CostImpactHigh,
    decimal? DownsideRisk,
    decimal? UpsidePotential,
    string? RequestedTier,
    string? WorkflowType);
