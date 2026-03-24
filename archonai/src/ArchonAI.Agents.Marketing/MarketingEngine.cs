using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using ArchonAI.Core.Models.Marketing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Agents.Marketing;

public sealed class MarketingEngine : IMarketingEngine
{
    private readonly IDataFabricEngine _dataFabric;
    private readonly IEventBus _eventBus;
    private readonly ILogger<MarketingEngine> _logger;
    private readonly MarketingOptions _options;

    private long _campaignsAnalyzed;
    private long _strategiesRecommended;
    private long _metricsGenerated;
    private long _dataFabricQueries;

    public MarketingEngine(
        IDataFabricEngine dataFabric,
        IEventBus eventBus,
        ILogger<MarketingEngine> logger,
        IOptions<MarketingOptions> options)
    {
        _dataFabric = dataFabric;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<CampaignAnalysisResult> AnalyzeCampaignAsync(
        string campaignId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Marketing.AnalyzeCampaign");
        activity?.SetTag("marketing.campaign_id", campaignId);

        Telemetry.MarketingCampaignAnalyses.Add(1);

        var fabricResult = await QueryMarketingDataAsync(campaignId, parameters, cancellationToken);

        double totalSpend = 0, totalRevenue = 0;
        int totalImpressions = 0, totalClicks = 0;
        int totalConversions = 0;

        foreach (var row in fabricResult.Rows)
        {
            if (row.TryGetValue("spend", out var spendStr) && double.TryParse(spendStr, out var s))
                totalSpend += s;
            if (row.TryGetValue("revenue", out var revStr) && double.TryParse(revStr, out var r))
                totalRevenue += r;
            if (row.TryGetValue("impressions", out var impStr) && int.TryParse(impStr, out var imp))
                totalImpressions += imp;
            if (row.TryGetValue("clicks", out var clkStr) && int.TryParse(clkStr, out var clk))
                totalClicks += clk;
            if (row.TryGetValue("conversions", out var convStr) && int.TryParse(convStr, out var conv))
                totalConversions += conv;
        }

        double roi = totalSpend > 0 ? (totalRevenue - totalSpend) / totalSpend : 0;
        double conversionRate = totalClicks > 0 ? (double)totalConversions / totalClicks : 0;

        var findings = new List<string>();
        var improvements = new List<string>();

        if (roi > _options.MinROIThreshold)
            findings.Add($"Campaign ROI of {roi:P1} exceeds threshold");
        else
            findings.Add($"Campaign ROI of {roi:P1} is below threshold of {_options.MinROIThreshold:P1}");

        if (totalClicks > 0 && totalImpressions > 0)
        {
            double ctr = (double)totalClicks / totalImpressions;
            if (ctr < 0.02)
                improvements.Add("Click-through rate is below 2% — consider revising ad creatives");
        }

        if (conversionRate < 0.05)
            improvements.Add("Conversion rate is below 5% — review landing page experience");

        if (totalSpend > 0 && roi < 0)
            improvements.Add("Campaign is operating at a loss — evaluate targeting strategy");

        var metrics = new Dictionary<string, string>
        {
            ["totalSpend"] = totalSpend.ToString("F2"),
            ["totalRevenue"] = totalRevenue.ToString("F2"),
            ["roi"] = roi.ToString("F4"),
            ["dataRows"] = fabricResult.Rows.Count.ToString()
        };

        Interlocked.Increment(ref _campaignsAnalyzed);
        _logger.LogInformation(
            "Campaign analysis for '{CampaignId}': ROI={ROI:P1}, impressions={Impressions}, clicks={Clicks}",
            campaignId, roi, totalImpressions, totalClicks);

        await EmitAuditEventAsync("marketing.campaign.analyzed", campaignId, cancellationToken);

        return new CampaignAnalysisResult(
            true, campaignId, totalSpend, totalRevenue, roi, conversionRate,
            totalImpressions, totalClicks, findings, improvements, metrics, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<MarketingStrategyRecommendation>> RecommendStrategiesAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Marketing.RecommendStrategies");

        Telemetry.MarketingStrategyRecommendations.Add(1);

        var fabricResult = await QueryMarketingDataAsync(scope, new Dictionary<string, string>(), cancellationToken);

        var channelPerformance = new Dictionary<string, (double revenue, double spend, int impressions)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in fabricResult.Rows)
        {
            string channel = row.GetValueOrDefault("channel", "unknown");
            double revenue = row.TryGetValue("revenue", out var rv) && double.TryParse(rv, out var r) ? r : 0;
            double spend = row.TryGetValue("spend", out var sp) && double.TryParse(sp, out var s) ? s : 0;
            int impressions = row.TryGetValue("impressions", out var im) && int.TryParse(im, out var i) ? i : 0;

            if (channelPerformance.TryGetValue(channel, out var existing))
                channelPerformance[channel] = (existing.revenue + revenue, existing.spend + spend, existing.impressions + impressions);
            else
                channelPerformance[channel] = (revenue, spend, impressions);
        }

        var recommendations = new List<MarketingStrategyRecommendation>();

        foreach (var (channel, perf) in channelPerformance.OrderByDescending(kv => kv.Value.revenue))
        {
            double channelROI = perf.spend > 0 ? (perf.revenue - perf.spend) / perf.spend : 0;
            double confidence = Math.Min(1.0, Math.Max(0.1, channelROI / 2.0));

            string strategyType = channelROI > _options.MinROIThreshold ? "scale-up" : "optimize";
            string title = channelROI > _options.MinROIThreshold
                ? $"Scale up {channel} channel"
                : $"Optimize {channel} channel performance";
            string description = channelROI > _options.MinROIThreshold
                ? $"Channel '{channel}' shows strong ROI of {channelROI:P1}. Increase budget allocation."
                : $"Channel '{channel}' has ROI of {channelROI:P1}. Review targeting and creative strategy.";

            recommendations.Add(new MarketingStrategyRecommendation(
                Guid.NewGuid(),
                strategyType,
                title,
                description,
                channelROI,
                confidence,
                new[] { channel },
                new Dictionary<string, string>
                {
                    ["revenue"] = perf.revenue.ToString("F2"),
                    ["spend"] = perf.spend.ToString("F2"),
                    ["impressions"] = perf.impressions.ToString()
                },
                DateTimeOffset.UtcNow));
        }

        int maxStrategies = _options.MaxStrategiesPerRecommendation;
        var result = recommendations.Take(maxStrategies).ToList();

        Interlocked.Add(ref _strategiesRecommended, result.Count);
        _logger.LogInformation(
            "Generated {Count} strategy recommendations for scope '{Scope}'",
            result.Count, scope);

        await EmitAuditEventAsync("marketing.strategies.recommended", scope, cancellationToken);

        return result;
    }

    public async global::System.Threading.Tasks.Task<EngagementMetricsSnapshot> GetEngagementMetricsAsync(
        string scope,
        string period,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Marketing.GetEngagementMetrics");

        Telemetry.MarketingMetricsGenerated.Add(1);

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(period))
            filters["period"] = period;

        var fabricResult = await QueryMarketingDataAsync(scope, filters, cancellationToken);

        int totalImpressions = 0, totalClicks = 0, totalConversions = 0;
        double totalCost = 0;
        var channelBreakdown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in fabricResult.Rows)
        {
            if (row.TryGetValue("impressions", out var impStr) && int.TryParse(impStr, out var imp))
                totalImpressions += imp;
            if (row.TryGetValue("clicks", out var clkStr) && int.TryParse(clkStr, out var clk))
                totalClicks += clk;
            if (row.TryGetValue("conversions", out var convStr) && int.TryParse(convStr, out var conv))
                totalConversions += conv;
            if (row.TryGetValue("cost", out var costStr) && double.TryParse(costStr, out var cost))
                totalCost += cost;

            if (row.TryGetValue("channel", out var channel))
            {
                string clicks = row.GetValueOrDefault("clicks", "0");
                channelBreakdown[channel] = clicks;
            }
        }

        double clickThroughRate = totalImpressions > 0 ? (double)totalClicks / totalImpressions : 0;
        double conversionRate = totalClicks > 0 ? (double)totalConversions / totalClicks : 0;
        double costPerAcquisition = totalConversions > 0 ? totalCost / totalConversions : 0;

        var additionalMetrics = new Dictionary<string, string>
        {
            ["totalCost"] = totalCost.ToString("F2"),
            ["dataRows"] = fabricResult.Rows.Count.ToString()
        };

        Interlocked.Increment(ref _metricsGenerated);
        _logger.LogInformation(
            "Engagement metrics for scope '{Scope}' period '{Period}': impressions={Impressions}, clicks={Clicks}, conversions={Conversions}",
            scope, period, totalImpressions, totalClicks, totalConversions);

        return new EngagementMetricsSnapshot(
            scope, period, totalImpressions, totalClicks, totalConversions,
            clickThroughRate, conversionRate, costPerAcquisition,
            channelBreakdown, additionalMetrics, DateTimeOffset.UtcNow);
    }

    public MarketingEngineStatus GetStatus()
    {
        return new MarketingEngineStatus(
            IsActive: true,
            CampaignsAnalyzed: Interlocked.Read(ref _campaignsAnalyzed),
            StrategiesRecommended: Interlocked.Read(ref _strategiesRecommended),
            MetricsGenerated: Interlocked.Read(ref _metricsGenerated),
            DataFabricQueries: Interlocked.Read(ref _dataFabricQueries),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<DataFabricQueryResult> QueryMarketingDataAsync(
        string scope,
        IReadOnlyDictionary<string, string> filters,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dataFabricQueries);
        Telemetry.MarketingDataFabricQueries.Add(1);

        string source = scope == "*"
            ? string.Join(",", _options.MarketingSources)
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
            Source: "marketing-engine",
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
