using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class GoogleWorkspaceConnectorTool : IAgentTool
{
    private readonly IGoogleWorkspaceConnector _connector;

    public GoogleWorkspaceConnectorTool(IGoogleWorkspaceConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.google-workspace";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string action = request.Parameters.GetValueOrDefault("action", "list-files");

        return action switch
        {
            "get-emails" => await GetEmailsAsync(request, cancellationToken),
            "send-email" => await SendEmailAsync(request, cancellationToken),
            "get-document" => await GetDocumentAsync(request, cancellationToken),
            "create-document" => await CreateDocumentAsync(request, cancellationToken),
            "read-spreadsheet" => await ReadSpreadsheetAsync(request, cancellationToken),
            "write-spreadsheet" => await WriteSpreadsheetAsync(request, cancellationToken),
            "list-files" => await ListFilesAsync(request, cancellationToken),
            "get-file" => await GetFileMetadataAsync(request, cancellationToken),
            "status" => GetStatus(),
            _ => new ToolExecutionResult(Name, false, new Dictionary<string, string>
            {
                ["error"] = $"Unknown Google Workspace action: {action}"
            }, [$"Unsupported action: {action}"], DateTimeOffset.UtcNow)
        };
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetEmailsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string? query = request.Parameters.GetValueOrDefault("query");
        _ = int.TryParse(request.Parameters.GetValueOrDefault("maxResults", "20"), out int maxResults);

        var messages = await _connector.GetEmailsAsync(query, maxResults, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "gmail",
            ["count"] = messages.Count.ToString(),
            ["messages"] = System.Text.Json.JsonSerializer.Serialize(messages)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> SendEmailAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string to = request.Parameters.GetValueOrDefault("to", string.Empty);
        string subject = request.Parameters.GetValueOrDefault("subject", string.Empty);
        string body = request.Parameters.GetValueOrDefault("body", string.Empty);
        bool isHtml = request.Parameters.GetValueOrDefault("isHtml", "false").Equals("true", StringComparison.OrdinalIgnoreCase);

        var result = await _connector.SendEmailAsync(to, subject, body, isHtml, ct);

        return new ToolExecutionResult(Name, result.IsSuccess, new Dictionary<string, string>
        {
            ["service"] = "gmail",
            ["operation"] = "send-email",
            ["messageId"] = result.MessageId ?? string.Empty,
            ["threadId"] = result.ThreadId ?? string.Empty
        }, result.Error is not null ? [result.Error] : Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetDocumentAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string documentId = request.Parameters.GetValueOrDefault("documentId", string.Empty);

        var doc = await _connector.GetDocumentAsync(documentId, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "docs",
            ["documentId"] = doc.DocumentId,
            ["title"] = doc.Title,
            ["bodyLength"] = (doc.BodyText?.Length ?? 0).ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> CreateDocumentAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string title = request.Parameters.GetValueOrDefault("title", string.Empty);
        string? content = request.Parameters.GetValueOrDefault("content");

        string documentId = await _connector.CreateDocumentAsync(title, content, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "docs",
            ["operation"] = "create-document",
            ["documentId"] = documentId
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> ReadSpreadsheetAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string spreadsheetId = request.Parameters.GetValueOrDefault("spreadsheetId", string.Empty);
        string range = request.Parameters.GetValueOrDefault("range", "Sheet1!A1:Z1000");

        var data = await _connector.ReadSpreadsheetAsync(spreadsheetId, range, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "sheets",
            ["spreadsheetId"] = data.SpreadsheetId,
            ["range"] = data.Range,
            ["rowCount"] = data.RowCount.ToString(),
            ["columnCount"] = data.ColumnCount.ToString(),
            ["values"] = System.Text.Json.JsonSerializer.Serialize(data.Values)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> WriteSpreadsheetAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string spreadsheetId = request.Parameters.GetValueOrDefault("spreadsheetId", string.Empty);
        string range = request.Parameters.GetValueOrDefault("range", "Sheet1!A1");

        // Parse row data from parameters: row.0.0, row.0.1, row.1.0, etc.
        var values = new List<IReadOnlyList<string>>();
        int rowIndex = 0;
        while (request.Parameters.ContainsKey($"row.{rowIndex}.0"))
        {
            var row = new List<string>();
            int colIndex = 0;
            while (request.Parameters.TryGetValue($"row.{rowIndex}.{colIndex}", out string? cellValue))
            {
                row.Add(cellValue);
                colIndex++;
            }
            values.Add(row);
            rowIndex++;
        }

        // Fallback: single-row shorthand via col.0, col.1, etc.
        if (values.Count == 0)
        {
            var row = new List<string>();
            int colIndex = 0;
            while (request.Parameters.TryGetValue($"col.{colIndex}", out string? cellValue))
            {
                row.Add(cellValue);
                colIndex++;
            }
            if (row.Count > 0)
                values.Add(row);
        }

        int updatedCells = await _connector.WriteSpreadsheetAsync(spreadsheetId, range, values, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "sheets",
            ["operation"] = "write-spreadsheet",
            ["spreadsheetId"] = spreadsheetId,
            ["updatedCells"] = updatedCells.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> ListFilesAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string? query = request.Parameters.GetValueOrDefault("query");
        _ = int.TryParse(request.Parameters.GetValueOrDefault("maxResults", "50"), out int maxResults);

        var files = await _connector.ListFilesAsync(query, maxResults, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "drive",
            ["count"] = files.Count.ToString(),
            ["files"] = System.Text.Json.JsonSerializer.Serialize(files)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetFileMetadataAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string fileId = request.Parameters.GetValueOrDefault("fileId", string.Empty);

        var file = await _connector.GetFileMetadataAsync(fileId, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "drive",
            ["fileId"] = file.Id,
            ["fileName"] = file.Name,
            ["mimeType"] = file.MimeType,
            ["sizeBytes"] = file.SizeBytes?.ToString() ?? "N/A"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private ToolExecutionResult GetStatus()
    {
        var status = _connector.GetStatus();
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["isConnected"] = status.IsConnected.ToString(),
            ["email"] = status.Email ?? "N/A",
            ["totalRequests"] = status.TotalRequests.ToString(),
            ["failedRequests"] = status.FailedRequests.ToString(),
            ["emailsSent"] = status.EmailsSent.ToString(),
            ["docsAccessed"] = status.DocsAccessed.ToString(),
            ["sheetsAccessed"] = status.SheetsAccessed.ToString(),
            ["driveOps"] = status.DriveOps.ToString(),
            ["rateLimitRemaining"] = status.RateLimitRemaining.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}
