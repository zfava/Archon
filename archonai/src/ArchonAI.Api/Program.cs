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
