using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using ArchonAI.Core.Models.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Agents.Support;

public sealed class SupportEngine : ISupportEngine
{
    private readonly IDataFabricEngine _dataFabric;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SupportEngine> _logger;
    private readonly SupportOptions _options;

    private long _ticketsAnalyzed;
    private long _recurringIssuesDetected;
    private long _autoResponsesGenerated;
    private long _dataFabricQueries;

    public SupportEngine(
        IDataFabricEngine dataFabric,
        IEventBus eventBus,
        ILogger<SupportEngine> logger,
        IOptions<SupportOptions> options)
    {
        _dataFabric = dataFabric;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<TicketAnalysisResult> AnalyzeTicketsAsync(
        string scope,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Support.AnalyzeTickets");
        activity?.SetTag("support.scope", scope);

        Telemetry.SupportTicketAnalyses.Add(1);

        var fabricResult = await QuerySupportDataAsync(scope, parameters, cancellationToken);

        int totalTickets = fabricResult.Rows.Count;
        int openTickets = 0, resolvedTickets = 0;
        double totalResolutionHours = 0;
        int resolutionCount = 0;
        double totalSatisfaction = 0;
        int satisfactionCount = 0;
        var categoryCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in fabricResult.Rows)
        {
            string status = row.GetValueOrDefault("status", "").ToLowerInvariant();
            if (status == "open") openTickets++;
            else if (status == "resolved") resolvedTickets++;

            if (row.TryGetValue("resolution_hours", out var rhStr) && double.TryParse(rhStr, out var rh))
            {
                totalResolutionHours += rh;
                resolutionCount++;
            }

            if (row.TryGetValue("satisfaction", out var satStr) && double.TryParse(satStr, out var sat))
            {
                totalSatisfaction += sat;
                satisfactionCount++;
            }

            if (row.TryGetValue("category", out var cat) && !string.IsNullOrEmpty(cat))
                categoryCount[cat] = categoryCount.GetValueOrDefault(cat) + 1;
        }

        double avgResolutionHours = resolutionCount > 0 ? totalResolutionHours / resolutionCount : 0;
        double satisfactionScore = satisfactionCount > 0 ? totalSatisfaction / satisfactionCount : 0;

        var topCategories = categoryCount
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => kv.Key)
            .ToList();

        var keyFindings = new List<string>();
        if (openTickets > totalTickets / 2 && totalTickets > 0)
            keyFindings.Add("More than half of tickets are still open");
        if (avgResolutionHours > 48)
            keyFindings.Add("Average resolution time exceeds 48 hours");
        if (satisfactionScore < 3.0 && satisfactionCount > 0)
            keyFindings.Add("Customer satisfaction is below average");

        var metrics = new Dictionary<string, string>
        {
            ["total_tickets"] = totalTickets.ToString(),
            ["open_tickets"] = openTickets.ToString(),
            ["resolved_tickets"] = resolvedTickets.ToString(),
            ["avg_resolution_hours"] = avgResolutionHours.ToString("F2"),
            ["satisfaction_score"] = satisfactionScore.ToString("F2"),
            ["dataRows"] = fabricResult.Rows.Count.ToString()
        };

        Interlocked.Increment(ref _ticketsAnalyzed);
        _logger.LogInformation(
            "Ticket analysis for scope '{Scope}': total={Total}, open={Open}, resolved={Resolved}, avgResolution={AvgHrs:F1}h",
            scope, totalTickets, openTickets, resolvedTickets, avgResolutionHours);

        await EmitAuditEventAsync("support.tickets.analyzed", scope, cancellationToken);

        return new TicketAnalysisResult(
            true, scope, totalTickets, openTickets, resolvedTickets,
            avgResolutionHours, satisfactionScore, topCategories, keyFindings, metrics,
            DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<RecurringIssue>> DetectRecurringIssuesAsync(
        string scope,
        int minOccurrences = 3,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Support.DetectRecurringIssues");

        Telemetry.SupportRecurringIssueScans.Add(1);

        var fabricResult = await QuerySupportDataAsync(scope, new Dictionary<string, string>(), cancellationToken);

        var grouped = fabricResult.Rows
            .Where(r => r.ContainsKey("category"))
            .GroupBy(r => r["category"])
            .Where(g => g.Count() >= minOccurrences)
            .ToList();

        var recurringIssues = new List<RecurringIssue>();

        foreach (var group in grouped)
        {
            int occurrences = group.Count();
            double severity = occurrences switch
            {
                >= 20 => 1.0,
                >= 10 => 0.8,
                >= 5 => 0.6,
                _ => 0.4
            };

            string title = group.First().GetValueOrDefault("title", group.Key);
            string description = $"Recurring issue in category '{group.Key}' with {occurrences} occurrences";
            string suggestedResolution = $"Investigate root cause for '{group.Key}' issues and implement preventive measures";

            recurringIssues.Add(new RecurringIssue(
                Guid.NewGuid(), group.Key, title, description, occurrences, severity,
                suggestedResolution,
                new Dictionary<string, string>
                {
                    ["category"] = group.Key,
                    ["occurrences"] = occurrences.ToString(),
                    ["severity"] = severity.ToString("F2")
                },
                DateTimeOffset.UtcNow));
        }

        Interlocked.Add(ref _recurringIssuesDetected, recurringIssues.Count);
        _logger.LogInformation(
            "Detected {Count} recurring issues for scope '{Scope}' with min occurrences {Min}",
            recurringIssues.Count, scope, minOccurrences);

        return recurringIssues;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<AutoResponseRecommendation>> RecommendAutoResponsesAsync(
        string issueCategory,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Support.RecommendAutoResponses");
        activity?.SetTag("support.issue_category", issueCategory);

        Telemetry.SupportAutoResponses.Add(1);

        var filters = new Dictionary<string, string> { ["category"] = issueCategory };
        var fabricResult = await QuerySupportDataAsync("*", filters, cancellationToken);

        var recommendations = new List<AutoResponseRecommendation>();

        var grouped = fabricResult.Rows
            .Where(r => r.ContainsKey("category"))
            .GroupBy(r => r["category"])
            .Take(_options.MaxAutoResponses);

        foreach (var group in grouped)
        {
            int matchingCount = group.Count();
            double confidence = Math.Min(1.0, matchingCount * 0.1 + 0.3);

            if (confidence < _options.MinAutoResponseConfidence)
                continue;

            string category = group.Key;
            string title = $"Auto-response for '{category}' issues";
            string suggestedResponse = $"Thank you for contacting support regarding '{category}'. " +
                "We have identified this as a known issue and are working on a resolution. " +
                "In the meantime, please try the following steps...";

            recommendations.Add(new AutoResponseRecommendation(
                Guid.NewGuid(), category, title, suggestedResponse, confidence, matchingCount,
                new Dictionary<string, string>
                {
                    ["category"] = category,
                    ["matching_tickets"] = matchingCount.ToString(),
                    ["confidence"] = confidence.ToString("F2"),
                    ["source"] = "pattern_analysis"
                },
                DateTimeOffset.UtcNow));
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new AutoResponseRecommendation(
                Guid.NewGuid(), issueCategory,
                $"Generic response for '{issueCategory}'",
                $"Thank you for reaching out about '{issueCategory}'. Our team is reviewing your request and will respond shortly.",
                0.5, 0,
                new Dictionary<string, string> { ["source"] = "default" },
                DateTimeOffset.UtcNow));
        }

        Interlocked.Add(ref _autoResponsesGenerated, recommendations.Count);
        _logger.LogInformation(
            "Generated {Count} auto-response recommendations for category '{Category}'",
            recommendations.Count, issueCategory);

        await EmitAuditEventAsync("support.autoresponses.generated", issueCategory, cancellationToken);

        return recommendations;
    }

    public SupportEngineStatus GetStatus()
    {
        return new SupportEngineStatus(
            IsActive: true,
            TicketsAnalyzed: Interlocked.Read(ref _ticketsAnalyzed),
            RecurringIssuesDetected: Interlocked.Read(ref _recurringIssuesDetected),
            AutoResponsesGenerated: Interlocked.Read(ref _autoResponsesGenerated),
            DataFabricQueries: Interlocked.Read(ref _dataFabricQueries),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<DataFabricQueryResult> QuerySupportDataAsync(
        string scope,
        IReadOnlyDictionary<string, string> filters,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dataFabricQueries);
        Telemetry.SupportDataFabricQueries.Add(1);

        string source = scope == "*"
            ? string.Join(",", _options.SupportSources)
            : scope;

        return await _dataFabric.QueryEnterpriseDataAsync(
            source: source,
            filters: filters,
            schemaMapping: new Dictionary<string, string>(),
            permissions: _options.DataFabricPermissions,
            consumerType: _options.DefaultConsumerType,
            cancellationToken);
    }

    private async global::System.Threading.Tasks.Task EmitAuditEventAsync(
        string eventType, string entityId, CancellationToken cancellationToken)
    {
        var auditEvent = new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: "support-engine",
            CorrelationId: Guid.NewGuid(),
            Payload: new Dictionary<string, string>
            {
                ["entityId"] = entityId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            },
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(auditEvent, cancellationToken);
    }
}
