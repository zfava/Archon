using ArchonAI.Agents.Sales;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Agents.Sales.Tests;

public sealed class SalesEngineTests
{
    private readonly IDataFabricEngine _dataFabric = Substitute.For<IDataFabricEngine>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<SalesEngine> _logger = Substitute.For<ILogger<SalesEngine>>();
    private readonly SalesOptions _options = new();

    private SalesEngine CreateEngine() =>
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
    public async Task AnalyzePipelineAsync_ReturnsResultWithCorrectCounts()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["value"] = "50000", ["probability"] = "0.8", ["stage"] = "proposal", ["winRate"] = "0.6" },
            new Dictionary<string, string> { ["value"] = "30000", ["probability"] = "0.5", ["stage"] = "negotiation", ["winRate"] = "0.7" },
            new Dictionary<string, string> { ["value"] = "20000", ["probability"] = "0.3", ["stage"] = "qualification", ["winRate"] = "0.4" }
        });

        var engine = CreateEngine();
        var result = await engine.AnalyzePipelineAsync("pipeline-1", new Dictionary<string, string>());

        result.IsSuccess.Should().BeTrue();
        result.TotalOpportunities.Should().Be(3);
        result.TotalValue.Should().Be(100000);
        result.WeightedValue.Should().BeApproximately(61000, 0.01);
        result.AverageCloseRate.Should().BeApproximately(0.567, 0.01);
    }

    [Fact]
    public async Task PrioritizeOpportunitiesAsync_ReturnsSortedOpportunities()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["opportunityId"] = "opp-1", ["name"] = "Small Deal", ["value"] = "10000", ["stage"] = "prospecting" },
            new Dictionary<string, string> { ["opportunityId"] = "opp-2", ["name"] = "Big Deal", ["value"] = "90000", ["stage"] = "negotiation" },
            new Dictionary<string, string> { ["opportunityId"] = "opp-3", ["name"] = "Medium Deal", ["value"] = "50000", ["stage"] = "proposal" }
        });

        var engine = CreateEngine();
        var result = await engine.PrioritizeOpportunitiesAsync("pipeline-1");

        result.Should().NotBeEmpty();
        result[0].OpportunityId.Should().Be("opp-2");
        result.Should().BeInDescendingOrder(o => o.PriorityScore);
    }

    [Fact]
    public async Task RecommendOutreachAsync_GeneratesRecommendations()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["opportunityId"] = "opp-1", ["stage"] = "proposal" },
            new Dictionary<string, string> { ["opportunityId"] = "opp-1", ["stage"] = "negotiation" }
        });

        var engine = CreateEngine();
        var result = await engine.RecommendOutreachAsync("opp-1");

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.ActionType == "follow-up");
        result.Should().Contain(r => r.ActionType == "executive-sponsor");
    }

    [Fact]
    public async Task RecommendOutreachAsync_ReturnsDefaultWhenNoData()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        var result = await engine.RecommendOutreachAsync("opp-unknown");

        result.Should().HaveCount(1);
        result[0].ActionType.Should().Be("research");
    }

    [Fact]
    public async Task GetMetricsAsync_ReturnsMetricsSnapshot()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["revenue"] = "50000", ["pipelineValue"] = "80000", ["status"] = "won", ["cycleLength"] = "30" },
            new Dictionary<string, string> { ["revenue"] = "40000", ["pipelineValue"] = "60000", ["status"] = "won", ["cycleLength"] = "45" },
            new Dictionary<string, string> { ["revenue"] = "0", ["pipelineValue"] = "30000", ["status"] = "lost", ["cycleLength"] = "20" }
        });

        var engine = CreateEngine();
        var result = await engine.GetMetricsAsync("*", "Q1-2026");

        result.TotalRevenue.Should().Be(90000);
        result.PipelineValue.Should().Be(170000);
        result.DealsWon.Should().Be(2);
        result.DealsLost.Should().Be(1);
        result.WinRate.Should().BeApproximately(0.667, 0.01);
        result.AverageDealSize.Should().Be(45000);
        result.AverageSalesCycle.Should().BeApproximately(31.67, 0.01);
    }

    [Fact]
    public async Task AnalyzePipelineAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.AnalyzePipelineAsync("pipeline-1", new Dictionary<string, string>());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "sales.pipeline.analyzed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecommendOutreachAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.RecommendOutreachAsync("opp-1");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "sales.outreach.recommended"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMetricsAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.GetMetricsAsync("*", "current");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "sales.metrics.generated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetStatus_ReturnsCurrentCounters()
    {
        var engine = CreateEngine();
        var status = engine.GetStatus();

        status.IsActive.Should().BeTrue();
        status.PipelineAnalyses.Should().Be(0);
        status.OpportunitiesPrioritized.Should().Be(0);
        status.OutreachRecommendations.Should().Be(0);
        status.MetricsGenerated.Should().Be(0);
        status.DataFabricQueries.Should().Be(0);
        status.StatusAsOfUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
