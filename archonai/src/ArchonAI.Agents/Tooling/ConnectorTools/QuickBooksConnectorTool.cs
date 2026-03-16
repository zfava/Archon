using System.Globalization;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class QuickBooksConnectorTool : IAgentTool
{
    private readonly IQuickBooksConnector _connector;

    public QuickBooksConnectorTool(IQuickBooksConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.quickbooks";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string action = request.Parameters.GetValueOrDefault("action", "get-reports");

        return action switch
        {
            "get-reports" => await GetReportsAsync(request, cancellationToken),
            "create-invoice" => await CreateInvoiceAsync(request, cancellationToken),
            "update-customer" => await UpdateCustomerAsync(request, cancellationToken),
            "get-transactions" => await GetTransactionsAsync(request, cancellationToken),
            "status" => GetStatus(),
            _ => new ToolExecutionResult(Name, false, new Dictionary<string, string>
            {
                ["error"] = $"Unknown QuickBooks action: {action}"
            }, [$"Unsupported action: {action}"], DateTimeOffset.UtcNow)
        };
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetReportsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string reportType = request.Parameters.GetValueOrDefault("reportType", "ProfitAndLoss");
        string? startDate = request.Parameters.GetValueOrDefault("startDate");
        string? endDate = request.Parameters.GetValueOrDefault("endDate");

        var records = await _connector.GetFinancialReportsAsync(reportType, startDate, endDate, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["reportType"] = reportType,
            ["count"] = records.Count.ToString(),
            ["records"] = System.Text.Json.JsonSerializer.Serialize(records)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> CreateInvoiceAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string customerId = request.Parameters.GetValueOrDefault("customerId", string.Empty);

        var lineItems = new List<QuickBooksLineItem>();
        int index = 0;
        while (request.Parameters.ContainsKey($"line.{index}.description"))
        {
            string desc = request.Parameters.GetValueOrDefault($"line.{index}.description", string.Empty);
            decimal amount = decimal.TryParse(request.Parameters.GetValueOrDefault($"line.{index}.amount", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0;
            decimal qty = decimal.TryParse(request.Parameters.GetValueOrDefault($"line.{index}.quantity", "1"), NumberStyles.Any, CultureInfo.InvariantCulture, out var q) ? q : 1;
            string? itemRef = request.Parameters.GetValueOrDefault($"line.{index}.itemRef");

            lineItems.Add(new QuickBooksLineItem(desc, amount, qty, itemRef));
            index++;
        }

        if (lineItems.Count == 0)
        {
            // Support single-line shorthand
            string desc = request.Parameters.GetValueOrDefault("description", "Service");
            decimal amount = decimal.TryParse(request.Parameters.GetValueOrDefault("amount", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out var a2) ? a2 : 0;
            decimal qty = decimal.TryParse(request.Parameters.GetValueOrDefault("quantity", "1"), NumberStyles.Any, CultureInfo.InvariantCulture, out var q2) ? q2 : 1;
            lineItems.Add(new QuickBooksLineItem(desc, amount, qty, null));
        }

        string invoiceId = await _connector.CreateInvoiceAsync(customerId, lineItems, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["invoiceId"] = invoiceId,
            ["customerId"] = customerId,
            ["operation"] = "create-invoice",
            ["lineItemCount"] = lineItems.Count.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> UpdateCustomerAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string customerId = request.Parameters.GetValueOrDefault("customerId", string.Empty);
        var fields = request.Parameters
            .Where(kv => kv.Key.StartsWith("field.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                kv => kv.Key["field.".Length..],
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);

        await _connector.UpdateCustomerAsync(customerId, fields, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["customerId"] = customerId,
            ["operation"] = "update-customer"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetTransactionsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string? accountId = request.Parameters.GetValueOrDefault("accountId");
        string? startDate = request.Parameters.GetValueOrDefault("startDate");
        string? endDate = request.Parameters.GetValueOrDefault("endDate");
        _ = int.TryParse(request.Parameters.GetValueOrDefault("limit", "100"), out int limit);

        var records = await _connector.GetTransactionHistoryAsync(accountId, startDate, endDate, limit, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["objectType"] = "Purchase",
            ["count"] = records.Count.ToString(),
            ["records"] = System.Text.Json.JsonSerializer.Serialize(records)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private ToolExecutionResult GetStatus()
    {
        var status = _connector.GetStatus();
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["isConnected"] = status.IsConnected.ToString(),
            ["companyId"] = status.CompanyId ?? "N/A",
            ["totalRequests"] = status.TotalRequests.ToString(),
            ["failedRequests"] = status.FailedRequests.ToString(),
            ["rateLimitRemaining"] = status.RateLimitRemaining.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}
