using System.Text.Json;
using ArchonAI.Api.Dtos;
using ArchonAI.Connectors;
using ArchonAI.Core.Interfaces;

namespace ArchonAI.Api.Endpoints;

public static class ConnectorEndpoints
{
    public static IEndpointRouteBuilder MapConnectorEndpoints(this IEndpointRouteBuilder v1)
    {
        MapSalesforceEndpoints(v1);
        MapHubSpotEndpoints(v1);
        MapQuickBooksEndpoints(v1);
        MapSlackEndpoints(v1);
        MapGoogleWorkspaceEndpoints(v1);
        MapMicrosoft365Endpoints(v1);
        MapIntegrationEndpoints(v1);
        return v1;
    }

    private static void MapSalesforceEndpoints(IEndpointRouteBuilder v1)
    {
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
    }

    private static void MapHubSpotEndpoints(IEndpointRouteBuilder v1)
    {
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
    }

    private static void MapQuickBooksEndpoints(IEndpointRouteBuilder v1)
    {
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
    }

    private static void MapSlackEndpoints(IEndpointRouteBuilder v1)
    {
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
    }

    private static void MapGoogleWorkspaceEndpoints(IEndpointRouteBuilder v1)
    {
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
    }

    private static void MapMicrosoft365Endpoints(IEndpointRouteBuilder v1)
    {
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
    }

    private static void MapIntegrationEndpoints(IEndpointRouteBuilder v1)
    {
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
            var connector = connectors.FirstOrDefault(c =>
                c.SystemName.Equals(connectorId, StringComparison.OrdinalIgnoreCase) ||
                c.SystemName.ToLowerInvariant().Replace(' ', '-') == connectorId);

            if (connector is null)
                return Results.NotFound(new { error = $"Connector '{connectorId}' not found." });

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
    }
}
