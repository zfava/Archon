using Serilog;
using ArchonAI.Agents;
using ArchonAI.Api.Security;
using ArchonAI.Connectors;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AuditLog;
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
using ArchonAI.WorkflowDesigner;
using ArchonAI.AgentRegistry;
using ArchonAI.Core.Models.AgentRegistry;

var builder = WebApplication.CreateBuilder(args)
    .AddArchonAIObservability();

builder.Services.AddArchonAISecurity(builder.Configuration);
builder.Services.AddArchonAIInfrastructure();
builder.Services.AddArchonAIConnectors(builder.Configuration);
builder.Services.AddArchonAIAgentTooling();
builder.Services.AddArchonAIPlugins(builder.Configuration);
builder.Services.AddArchonAIRegistry();
builder.Services.AddArchonAIRuntime();
builder.Services.AddArchonAIOperations(builder.Configuration);
builder.Services.AddArchonAIFinance(builder.Configuration);
builder.Services.AddArchonAISales(builder.Configuration);
builder.Services.AddArchonAIMarketing(builder.Configuration);
builder.Services.AddArchonAISupport(builder.Configuration);
builder.Services.AddArchonAIAdmin(builder.Configuration);
builder.Services.AddArchonAIControlPlane(builder.Configuration);
builder.Services.AddArchonAIWorkflowDesigner();
builder.Services.AddArchonAIAgentRegistry();

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

admin.MapPost("/workflows/{workflowId:guid}/cancel", async (Guid workflowId, IAdminService adminService, CancellationToken ct) =>
{
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
