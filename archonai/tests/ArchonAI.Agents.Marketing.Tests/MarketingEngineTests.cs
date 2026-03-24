using ArchonAI.Agents.Marketing;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Agents.Marketing.Tests;

public sealed class MarketingEngineTests
{
    private readonly IDataFabricEngine _dataFabric = Substitute.For<IDataFabricEngine>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<MarketingEngine> _logger = Substitute.For<ILogger<MarketingEngine>>();
    private readonly MarketingOptions _options = new();

    private MarketingEngine CreateEngine() =>
        new(_dataFabric, _eventBus, _logger, Options.Create(_options));

    private void SetupFabricResult(IReadOnlyDictionary<string, string>[] rows)
    {
        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok", rows,
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AnalyzeCampaignAsync_ReturnsAnalysisWithROI()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["spend"] = "5000", ["revenue"] = "15000", ["impressions"] = "100000", ["clicks"] = "5000", ["conversions"] = "500" }
        });

        var engine = CreateEngine();
        var result = await engine.AnalyzeCampaignAsync("camp-1", new Dictionary<string, string>());

        result.IsSuccess.Should().BeTrue();
        result.CampaignId.Should().Be("camp-1");
        result.TotalSpend.Should().Be(5000);
        result.TotalRevenue.Should().Be(15000);
        result.ROI.Should().BeApproximately(2.0, 0.01);
        result.ConversionRate.Should().BeApproximately(0.1, 0.01);
        result.TotalImpressions.Should().Be(100000);
        result.TotalClicks.Should().Be(5000);
        result.KeyFindings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnalyzeCampaignAsync_DetectsImprovements()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["spend"] = "10000", ["revenue"] = "5000", ["impressions"] = "100000", ["clicks"] = "500", ["conversions"] = "10" }
        });

        var engine = CreateEngine();
        var result = await engine.AnalyzeCampaignAsync("camp-2", new Dictionary<string, string>());

        result.Improvements.Should().NotBeEmpty();
        result.ROI.Should().BeLessThan(0);
    }

    [Fact]
    public async Task AnalyzeCampaignAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.AnalyzeCampaignAsync("camp-1", new Dictionary<string, string>());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "marketing.campaign.analyzed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecommendStrategiesAsync_ReturnsRecommendations()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["channel"] = "email", ["revenue"] = "20000", ["spend"] = "5000", ["impressions"] = "50000" },
            new Dictionary<string, string> { ["channel"] = "social", ["revenue"] = "8000", ["spend"] = "6000", ["impressions"] = "80000" }
        });

        var engine = CreateEngine();
        var strategies = await engine.RecommendStrategiesAsync("*");

        strategies.Should().NotBeEmpty();
        strategies.Should().HaveCountLessOrEqualTo(_options.MaxStrategiesPerRecommendation);
        strategies[0].TargetChannels.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RecommendStrategiesAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.RecommendStrategiesAsync("*");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "marketing.strategies.recommended"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEngagementMetricsAsync_ReturnsMetrics()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["impressions"] = "50000", ["clicks"] = "2500", ["conversions"] = "250", ["cost"] = "5000", ["channel"] = "email" },
            new Dictionary<string, string> { ["impressions"] = "30000", ["clicks"] = "1500", ["conversions"] = "100", ["cost"] = "3000", ["channel"] = "social" }
        });

        var engine = CreateEngine();
        var metrics = await engine.GetEngagementMetricsAsync("*", "Q1-2026");

        metrics.Scope.Should().Be("*");
        metrics.Period.Should().Be("Q1-2026");
        metrics.TotalImpressions.Should().Be(80000);
        metrics.TotalClicks.Should().Be(4000);
        metrics.TotalConversions.Should().Be(350);
        metrics.ClickThroughRate.Should().BeApproximately(0.05, 0.001);
        metrics.ConversionRate.Should().BeApproximately(0.0875, 0.001);
        metrics.CostPerAcquisition.Should().BeApproximately(22.857, 0.01);
        metrics.ChannelBreakdown.Should().ContainKey("email");
        metrics.ChannelBreakdown.Should().ContainKey("social");
    }

    [Fact]
    public async Task GetEngagementMetricsAsync_ReturnsEmptyWhenNoData()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        var metrics = await engine.GetEngagementMetricsAsync("*", "current");

        metrics.TotalImpressions.Should().Be(0);
        metrics.TotalClicks.Should().Be(0);
        metrics.TotalConversions.Should().Be(0);
        metrics.ClickThroughRate.Should().Be(0);
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        var engine = CreateEngine();
        var status = engine.GetStatus();

        status.IsActive.Should().BeTrue();
        status.CampaignsAnalyzed.Should().Be(0);
        status.StrategiesRecommended.Should().Be(0);
        status.MetricsGenerated.Should().Be(0);
        status.DataFabricQueries.Should().Be(0);
        status.StatusAsOfUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
