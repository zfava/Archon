using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using ArchonAI.Core.Models.Sales;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Agents.Sales;

public sealed class SalesEngine : ISalesEngine
{
    private readonly IDataFabricEngine _dataFabric;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SalesEngine> _logger;
    private readonly SalesOptions _options;

    private long _pipelineAnalyses;
    private long _opportunitiesPrioritized;
    private long _outreachRecommendations;
    private long _metricsGenerated;
    private long _dataFabricQueries;
    private long _auditEventsEmitted;

    public SalesEngine(
        IDataFabricEngine dataFabric,
        IEventBus eventBus,
        ILogger<SalesEngine> logger,
        IOptions<SalesOptions> options)
    {
        _dataFabric = dataFabric;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<PipelineAnalysisResult> AnalyzePipelineAsync(
        string pipelineId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Sales.AnalyzePipeline");
        activity?.SetTag("sales.pipeline_id", pipelineId);

        Telemetry.SalesPipelineAnalyses.Add(1);

        var fabricResult = await QueryCrmDataAsync(pipelineId, parameters, cancellationToken);

        int totalOpportunities = fabricResult.Rows.Count;
        double totalValue = 0;
        double weightedValue = 0;
        double totalWinRate = 0;
        int winRateCount = 0;
        var stageDistribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var risks = new List<string>();

        foreach (var row in fabricResult.Rows)
        {
            if (row.TryGetValue("value", out var valStr) && double.TryParse(valStr, out var value))
                totalValue += value;

            double weight = 1.0;
            if (row.TryGetValue("probability", out var probStr) && double.TryParse(probStr, out var prob))
                weight = prob;

            if (row.TryGetValue("value", out var wValStr) && double.TryParse(wValStr, out var wVal))
                weightedValue += wVal * weight;

            if (row.TryGetValue("winRate", out var wrStr) && double.TryParse(wrStr, out var wr))
            {
                totalWinRate += wr;
                winRateCount++;
            }

            string stage = row.GetValueOrDefault("stage", "unknown");
            stageDistribution[stage] = stageDistribution.GetValueOrDefault(stage) + 1;
        }

        double averageCloseRate = winRateCount > 0 ? totalWinRate / winRateCount : 0;

        if (averageCloseRate < 0.2 && totalOpportunities > 0)
            risks.Add("Low average close rate across pipeline");

        if (totalOpportunities > 0 && weightedValue / totalValue < 0.3)
            risks.Add("Low weighted pipeline value indicates high-risk opportunities");

        var stageList = stageDistribution
            .Select(kv => $"{kv.Key}: {kv.Value}")
            .ToList();

        var metrics = new Dictionary<string, string>
        {
            ["totalValue"] = totalValue.ToString("F2"),
            ["weightedValue"] = weightedValue.ToString("F2"),
            ["dataRows"] = fabricResult.Rows.Count.ToString()
        };

        Interlocked.Increment(ref _pipelineAnalyses);
        _logger.LogInformation(
            "Pipeline analysis for '{PipelineId}': opportunities={Count}, totalValue={Value:F2}, weightedValue={Weighted:F2}",
            pipelineId, totalOpportunities, totalValue, weightedValue);

        await EmitAuditEventAsync("sales.pipeline.analyzed", pipelineId, cancellationToken);

        return new PipelineAnalysisResult(
            true, pipelineId, totalOpportunities, totalValue, weightedValue,
            averageCloseRate, stageList, risks, metrics, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<PrioritizedOpportunity>> PrioritizeOpportunitiesAsync(
        string pipelineId,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Sales.PrioritizeOpportunities");

        Telemetry.SalesOpportunitiesPrioritized.Add(1);

        var fabricResult = await QueryCrmDataAsync(pipelineId, new Dictionary<string, string>(), cancellationToken);

        var opportunities = new List<PrioritizedOpportunity>();

        foreach (var row in fabricResult.Rows)
        {
            string oppId = row.GetValueOrDefault("opportunityId", Guid.NewGuid().ToString());
            string name = row.GetValueOrDefault("name", "Unknown Opportunity");
            string stage = row.GetValueOrDefault("stage", "unknown");

            double value = 0;
            if (row.TryGetValue("value", out var valStr) && double.TryParse(valStr, out var v))
                value = v;

            // Score based on value, stage, and recency
            double valueScore = Math.Min(1.0, value / 100000.0);

            double stageScore = stage.ToLowerInvariant() switch
            {
                "negotiation" => 0.9,
                "proposal" => 0.7,
                "qualification" => 0.5,
                "discovery" => 0.3,
                "prospecting" => 0.1,
                _ => 0.4
            };

            double recencyScore = 0.5;
            if (row.TryGetValue("lastActivity", out var lastStr) &&
                DateTimeOffset.TryParse(lastStr, out var lastActivity))
            {
                double daysSince = (DateTimeOffset.UtcNow - lastActivity).TotalDays;
                recencyScore = Math.Max(0, 1.0 - (daysSince / 90.0));
            }

            double priorityScore = (valueScore * 0.4) + (stageScore * 0.35) + (recencyScore * 0.25);

            DateTimeOffset? expectedClose = null;
            if (row.TryGetValue("expectedCloseDate", out var closeDateStr) &&
                DateTimeOffset.TryParse(closeDateStr, out var closeDate))
                expectedClose = closeDate;

            string reason = $"Value: {valueScore:F2}, Stage ({stage}): {stageScore:F2}, Recency: {recencyScore:F2}";

            if (priorityScore >= _options.MinPriorityScore)
            {
                opportunities.Add(new PrioritizedOpportunity(
                    oppId, name, value, priorityScore, stage, reason, expectedClose,
                    new Dictionary<string, string>
                    {
                        ["valueScore"] = valueScore.ToString("F4"),
                        ["stageScore"] = stageScore.ToString("F4"),
                        ["recencyScore"] = recencyScore.ToString("F4")
                    }));
            }
        }

        int clampedMax = Math.Clamp(maxResults, 1, _options.MaxOpportunitiesPerPrioritization);
        var result = opportunities.OrderByDescending(o => o.PriorityScore).Take(clampedMax).ToList();

        Interlocked.Add(ref _opportunitiesPrioritized, result.Count);
        _logger.LogInformation(
            "Prioritized {Count} opportunities for pipeline '{PipelineId}'",
            result.Count, pipelineId);

        return result;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OutreachRecommendation>> RecommendOutreachAsync(
        string opportunityId,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Sales.RecommendOutreach");
        activity?.SetTag("sales.opportunity_id", opportunityId);

        Telemetry.SalesOutreachRecommendations.Add(1);

        var filters = new Dictionary<string, string> { ["opportunityId"] = opportunityId };
        var fabricResult = await QueryCrmDataAsync("*", filters, cancellationToken);

        var recommendations = new List<OutreachRecommendation>();

        foreach (var row in fabricResult.Rows)
        {
            string stage = row.GetValueOrDefault("stage", "unknown").ToLowerInvariant();
            string oppId = row.GetValueOrDefault("opportunityId", opportunityId);

            var (actionType, title, description, impact, timing) = stage switch
            {
                "proposal" => (
                    "follow-up",
                    "Follow up on proposal",
                    "Send a follow-up to check status of the proposal and address any concerns.",
                    0.7,
                    "within 2 days"),
                "negotiation" => (
                    "executive-sponsor",
                    "Engage executive sponsor",
                    "Bring in executive sponsor to help close the deal and address high-level concerns.",
                    0.9,
                    "within 1 day"),
                "qualification" => (
                    "demo",
                    "Schedule product demonstration",
                    "Offer a tailored demo to showcase relevant features and address qualification criteria.",
                    0.6,
                    "within 1 week"),
                "discovery" => (
                    "needs-assessment",
                    "Conduct needs assessment",
                    "Schedule a discovery call to understand specific pain points and requirements.",
                    0.5,
                    "within 1 week"),
                "prospecting" => (
                    "initial-outreach",
                    "Send personalized introduction",
                    "Craft a personalized outreach message highlighting relevant value propositions.",
                    0.3,
                    "within 3 days"),
                _ => (
                    "check-in",
                    "General check-in",
                    "Reach out for a general status update and relationship nurture.",
                    0.4,
                    "within 1 week")
            };

            recommendations.Add(new OutreachRecommendation(
                Guid.NewGuid(), oppId, actionType, title, description, impact, timing,
                new Dictionary<string, string>
                {
                    ["stage"] = stage,
                    ["source"] = "sales-engine"
                },
                DateTimeOffset.UtcNow));
        }

        // If no data rows, provide a default recommendation
        if (recommendations.Count == 0)
        {
            recommendations.Add(new OutreachRecommendation(
                Guid.NewGuid(), opportunityId, "research",
                "Research opportunity",
                "No CRM data available. Research the opportunity and update CRM records.",
                0.5, "immediately",
                new Dictionary<string, string> { ["source"] = "sales-engine" },
                DateTimeOffset.UtcNow));
        }

        int clampedMax = Math.Clamp(recommendations.Count, 0, _options.MaxOutreachRecommendations);
        var result = recommendations.Take(clampedMax).ToList();

        Interlocked.Add(ref _outreachRecommendations, result.Count);
        _logger.LogInformation(
            "Generated {Count} outreach recommendations for opportunity '{OpportunityId}'",
            result.Count, opportunityId);

        await EmitAuditEventAsync("sales.outreach.recommended", opportunityId, cancellationToken);

        return result;
    }

    public async global::System.Threading.Tasks.Task<SalesMetricsSnapshot> GetMetricsAsync(
        string scope,
        string period,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Sales.GetMetrics");

        Telemetry.SalesMetricsGenerated.Add(1);

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(period))
            filters["period"] = period;

        var fabricResult = await QueryCrmDataAsync(scope, filters, cancellationToken);

        double totalRevenue = 0;
        double pipelineValue = 0;
        int dealsWon = 0;
        int dealsLost = 0;
        double totalCycleLength = 0;
        int cycleCount = 0;

        foreach (var row in fabricResult.Rows)
        {
            if (row.TryGetValue("revenue", out var revStr) && double.TryParse(revStr, out var rev))
                totalRevenue += rev;

            if (row.TryGetValue("pipelineValue", out var pvStr) && double.TryParse(pvStr, out var pv))
                pipelineValue += pv;

            string status = row.GetValueOrDefault("status", "").ToLowerInvariant();
            if (status == "won") dealsWon++;
            else if (status == "lost") dealsLost++;

            if (row.TryGetValue("cycleLength", out var clStr) && double.TryParse(clStr, out var cl))
            {
                totalCycleLength += cl;
                cycleCount++;
            }
        }

        int totalDecided = dealsWon + dealsLost;
        double winRate = totalDecided > 0 ? (double)dealsWon / totalDecided : 0;
        double averageDealSize = dealsWon > 0 ? totalRevenue / dealsWon : 0;
        double averageSalesCycle = cycleCount > 0 ? totalCycleLength / cycleCount : 0;

        var additionalMetrics = new Dictionary<string, string>
        {
            ["dataRows"] = fabricResult.Rows.Count.ToString(),
            ["totalDecided"] = totalDecided.ToString()
        };

        Interlocked.Increment(ref _metricsGenerated);
        _logger.LogInformation(
            "Sales metrics for scope '{Scope}' period '{Period}': revenue={Revenue:F2}, winRate={WinRate:P1}, deals={Won}/{Lost}",
            scope, period, totalRevenue, winRate, dealsWon, dealsLost);

        await EmitAuditEventAsync("sales.metrics.generated", scope, cancellationToken);

        return new SalesMetricsSnapshot(
            scope, period, totalRevenue, pipelineValue, dealsWon, dealsLost,
            winRate, averageDealSize, averageSalesCycle, additionalMetrics,
            DateTimeOffset.UtcNow);
    }

    public SalesEngineStatus GetStatus()
    {
        return new SalesEngineStatus(
            IsActive: true,
            PipelineAnalyses: Interlocked.Read(ref _pipelineAnalyses),
            OpportunitiesPrioritized: Interlocked.Read(ref _opportunitiesPrioritized),
            OutreachRecommendations: Interlocked.Read(ref _outreachRecommendations),
            MetricsGenerated: Interlocked.Read(ref _metricsGenerated),
            DataFabricQueries: Interlocked.Read(ref _dataFabricQueries),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<DataFabricQueryResult> QueryCrmDataAsync(
        string scope,
        IReadOnlyDictionary<string, string> filters,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dataFabricQueries);
        Telemetry.SalesDataFabricQueries.Add(1);

        string source = scope == "*"
            ? string.Join(",", _options.CrmSources)
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
        Interlocked.Increment(ref _auditEventsEmitted);

        var auditEvent = new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: "sales-engine",
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
